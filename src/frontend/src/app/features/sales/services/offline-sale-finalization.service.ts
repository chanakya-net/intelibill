import { Injectable, inject } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import {
  InvoiceLeaseExhaustedError,
  InvoiceLeaseExpiredError,
  InvoiceLeaseIndexedDbService,
  InvoiceLeaseNotFoundError,
  type InvoiceLeaseSnapshot,
} from '../../../core/storage/invoice-lease-indexeddb.service';
import {
  OfflineSalesSnapshotIndexedDbService,
  type OfflineSellableBatchSnapshot,
  type OfflineSellableServiceSnapshot,
} from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { buildOfflineInvoiceDocument, fiscalYearStartOf } from './offline-invoice-document.builder';
import { evaluateOfflineSaleEconomics } from './offline-sale-economics';
import { calculateOfflineFrozenSale } from './offline-sale-pricing-calculator';
import {
  OfflineFrozenSalePricing,
  OfflineSalePricingInput,
  OfflineSalePricingLineInput,
  OfflineQueuedSalePayload,
} from './offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { OfflineSalesShadowStockIndexedDbService } from './offline-sales-shadow-stock-indexeddb.service';
import { fitsQuantityScale, normalizeQuantity } from '../../../shared/quantity/quantity-input';
import { QUANTITY_SCALE_MAX } from '../../../shared/quantity/quantity-units';

export interface OfflineFinalizeRequest {
  readonly tillEvidence?: Pick<
    OfflineQueuedSalePayload,
    'till' | 'tenderLines' | 'tillBusinessDate' | 'tillCashierId'
  >;
  readonly shopId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly pricingInput: OfflineSalePricingInput;
  readonly maxSnapshotAgeMs: number;
}

export type OfflineFinalizeFailureReason =
  | 'TILL_CONTEXT_INVALID'
  | 'BELOW_RECORDED_COST'
  | 'SNAPSHOT_STALE'
  | 'MISSING_CATALOG_ITEM'
  | 'INSUFFICIENT_SHADOW_STOCK'
  | 'MISSING_DUE_CUSTOMER'
  | 'INVOICE_UNAVAILABLE'
  | 'INVALID_AMOUNT'
  /** The lease predates offline documents: the device must be enabled again online before it issues. */
  | 'SELLER_PROFILE_UNAVAILABLE'
  /** The sale cannot be issued as a GST document (the server's Invoice.* code says why). */
  | 'DOCUMENT_UNAVAILABLE'
  /** The selected customer is not in the offline snapshot, so the device cannot print their particulars. */
  | 'CUSTOMER_UNAVAILABLE'
  /**
   * A quantity needs more decimals than the snapshot's unit allows: a batch from a snapshot taken before units sells
   * whole units only until the snapshot is refreshed online.
   */
  | 'QUANTITY_PRECISION_EXCEEDED';

export type OfflineFinalizeResult =
  | {
      readonly ok: true;
      readonly payload: OfflineQueuedSalePayload;
      readonly remainingInvoiceCount: number;
    }
  | {
      readonly ok: false;
      readonly reason: OfflineFinalizeFailureReason;
      readonly errorCode?: string;
    };

@Injectable({ providedIn: 'root' })
export class OfflineSaleFinalizationService {
  private readonly auth = inject(AuthService);
  private readonly snapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly leaseDb = inject(InvoiceLeaseIndexedDbService);
  private readonly queueDb = inject(OfflineSalesQueueIndexedDbService);
  private readonly shadowDb = inject(OfflineSalesShadowStockIndexedDbService);

  async finalizeAndQueue(request: OfflineFinalizeRequest): Promise<OfflineFinalizeResult> {
    const owner = this.auth.session();
    const issuedByUserId = owner?.user.id || null;
    const ownerShopId = owner?.activeShopId ?? null;
    const frozenTill = structuredClone(request.tillEvidence ?? {});
    const hasTillFields = Object.keys(frozenTill).length > 0;
    if (
      hasTillFields &&
      (!frozenTill.till ||
        !frozenTill.tenderLines ||
        !frozenTill.tillBusinessDate ||
        !issuedByUserId ||
        frozenTill.tillCashierId !== issuedByUserId ||
        ownerShopId !== request.shopId)
    )
      return { ok: false, reason: 'TILL_CONTEXT_INVALID' };
    const snapshot = await this.snapshotDb.getUsableSnapshotInfo(request.shopId);
    if (!snapshot?.completedAt) return { ok: false, reason: 'SNAPSHOT_STALE' };

    const snapshotAgeMs = Date.now() - Date.parse(snapshot.completedAt);
    if (Number.isNaN(snapshotAgeMs) || snapshotAgeMs > request.maxSnapshotAgeMs) {
      return { ok: false, reason: 'SNAPSHOT_STALE' };
    }

    const batches = await this.snapshotDb.getUsableBatches(request.shopId);
    const byBatchId = new Map(batches.map((batch) => [batch.batchId, batch]));

    const services = await this.snapshotDb.getUsableServices(request.shopId);
    const byServiceId = new Map(services.map((svc) => [svc.serviceId, svc]));

    const requestedByBatch = new Map<string, number>();
    for (const line of request.pricingInput.lines) {
      if (line.lineType === 'service') continue;
      const batch = byBatchId.get(line.inventoryBatchId);
      if (!batch) return { ok: false, reason: 'MISSING_CATALOG_ITEM' };
      // A non-finite quantity is refused as an invalid amount when the sale is priced.
      if (
        Number.isFinite(line.quantity) &&
        !fitsQuantityScale(line.quantity, batch.quantityScale ?? 0)
      ) {
        return { ok: false, reason: 'QUANTITY_PRECISION_EXCEEDED' };
      }
      // Lines of one batch add up exactly: 0.1 + 0.2 is 0.3, not 0.30000000000000004.
      requestedByBatch.set(
        line.inventoryBatchId,
        normalizeQuantity(
          (requestedByBatch.get(line.inventoryBatchId) ?? 0) + line.quantity,
          QUANTITY_SCALE_MAX,
        ),
      );
    }

    for (const [inventoryBatchId, requestedQuantity] of requestedByBatch) {
      const batch = byBatchId.get(inventoryBatchId);
      if (!batch) return { ok: false, reason: 'MISSING_CATALOG_ITEM' };

      const available = await this.shadowDb.ensureQuantity(
        request.shopId,
        request.deviceId,
        inventoryBatchId,
        batch.quantity,
      );
      if (available < requestedQuantity) return { ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' };
    }

    for (const line of request.pricingInput.lines) {
      if (line.lineType === 'service') {
        if (!line.serviceId || !byServiceId.has(line.serviceId)) {
          return { ok: false, reason: 'MISSING_CATALOG_ITEM' };
        }
      }
    }

    const pricingInputFromSnapshot: OfflineSalePricingInput = {
      ...request.pricingInput,
      lines: request.pricingInput.lines.map((line) => {
        if (line.lineType === 'service') {
          return this.buildServicePricingLine(line, byServiceId.get(line.serviceId!)!);
        }
        return this.buildSnapshotPricingLine(line, byBatchId.get(line.inventoryBatchId)!);
      }),
      rules: await this.snapshotDb.getUsableDiscountRules(request.shopId),
    };

    let priced: OfflineFrozenSalePricing;
    try {
      priced = withDocumentUnits(calculateOfflineFrozenSale(pricingInputFromSnapshot), byBatchId);
    } catch (error) {
      // A non-finite quantity, price or discount has no place in the integer contract; nothing is consumed yet.
      if (error instanceof RangeError) return { ok: false, reason: 'INVALID_AMOUNT' };
      throw error;
    }

    if (evaluateOfflineSaleEconomics(priced).belowCostLineIds.length > 0) {
      return { ok: false, reason: 'BELOW_RECORDED_COST' };
    }

    const customerId = request.pricingInput.customerId;
    const customer = customerId
      ? ((await this.snapshotDb.getUsableCustomers(request.shopId)).find(
          (c) => c.customerId === customerId,
        ) ?? null)
      : null;
    if (priced.totals.dueAmount > 0 && !customer)
      return { ok: false, reason: 'MISSING_DUE_CUSTOMER' };
    // The server issues to the customer's own record on sync; a device that cannot see it would print a different
    // buyer (perhaps a walk-in without the buyer's GSTIN) on the copy it hands over.
    if (customerId && !customer) return { ok: false, reason: 'CUSTOMER_UNAVAILABLE' };

    const clientSaleId = crypto.randomUUID();
    const idempotencyKey = `offline-sale-${clientSaleId}`;

    let invoiceNumber: string;
    let consumedLeaseNextNumber: number;
    let consumedLease: InvoiceLeaseSnapshot;
    let remainingInvoiceCount = 0;
    try {
      const consumed = await this.leaseDb.consumeNextInvoiceNumber(
        request.shopId,
        request.deviceId,
        request.fiscalYear,
      );
      invoiceNumber = consumed.invoiceNumber;
      consumedLeaseNextNumber = consumed.lease.nextNumber;
      consumedLease = consumed.lease;
      remainingInvoiceCount = consumed.remainingCount;
    } catch (error) {
      if (
        error instanceof InvoiceLeaseNotFoundError ||
        error instanceof InvoiceLeaseExpiredError ||
        error instanceof InvoiceLeaseExhaustedError
      ) {
        return { ok: false, reason: 'INVOICE_UNAVAILABLE' };
      }
      return { ok: false, reason: 'INVOICE_UNAVAILABLE' };
    }

    // The document is frozen once, here, under the identity the lease froze: the server issues its own copy from the
    // same profile on sync, and no later shop or customer edit reaches a reprint. A sale that cannot be issued gives
    // its number back before anything is handed over.
    const sellerProfile = consumedLease.sellerProfile;
    const fiscalYearStart = fiscalYearStartOf(consumedLease.fiscalYear ?? request.fiscalYear);
    if (!sellerProfile || fiscalYearStart === null) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber);
      return { ok: false, reason: 'SELLER_PROFILE_UNAVAILABLE' };
    }

    const built = buildOfflineInvoiceDocument({
      sellerProfile,
      fiscalYearStart,
      soldAt: request.pricingInput.soldAt,
      customer,
      customerName: request.pricingInput.customerName,
      customerPhone: request.pricingInput.customerPhone,
      pricing: priced,
    });
    if (!built.ok) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber);
      return { ok: false, reason: 'DOCUMENT_UNAVAILABLE', errorCode: built.errorCode };
    }

    const payload: OfflineQueuedSalePayload = {
      ...frozenTill,
      clientSaleId,
      idempotencyKey,
      shopId: request.shopId,
      deviceId: request.deviceId,
      invoiceNumber,
      soldAt: request.pricingInput.soldAt,
      pricing: priced,
      paymentMethod: request.pricingInput.paymentMethod,
      customerId: request.pricingInput.customerId,
      customerName: request.pricingInput.customerName,
      customerPhone: request.pricingInput.customerPhone,
      ...(issuedByUserId ? { issuedByUserId } : {}),
      document: { ...built.draft, number: invoiceNumber },
    };

    if (
      (this.auth.session()?.user.id || null) !== issuedByUserId ||
      (this.auth.session()?.activeShopId ?? null) !== ownerShopId
    ) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber);
      throw new Error('Offline invoice owner changed before persistence.');
    }
    try {
      await this.queueDb.savePendingSale({
        shopId: request.shopId,
        deviceId: request.deviceId,
        clientSaleId,
        idempotencyKey,
        invoiceNumber,
        soldAt: request.pricingInput.soldAt,
        payload,
      });
    } catch (error) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber);
      throw error;
    }

    for (const line of request.pricingInput.lines) {
      if (line.lineType === 'service') continue;
      await this.shadowDb.reduceQuantity(
        request.shopId,
        request.deviceId,
        line.inventoryBatchId,
        line.quantity,
      );
    }

    return { ok: true, payload, remainingInvoiceCount };
  }

  private async releaseInvoiceNumber(
    request: OfflineFinalizeRequest,
    consumedLeaseNextNumber: number,
  ): Promise<void> {
    await this.leaseDb.rollbackConsumedInvoiceNumber(
      request.shopId,
      request.deviceId,
      request.fiscalYear,
      consumedLeaseNextNumber,
    );
  }

  private buildServicePricingLine(
    line: OfflineSalePricingLineInput,
    service: OfflineSellableServiceSnapshot,
  ): OfflineSalePricingLineInput {
    return {
      clientLineId: line.clientLineId,
      inventoryBatchId: line.serviceId ?? line.inventoryBatchId,
      itemId: service.serviceId,
      barcode: service.code ?? service.serviceId,
      itemName: service.name,
      batchNumber: '',
      quantity: line.quantity,
      salesPrice: service.price,
      mrp: 0,
      costPrice: 0,
      taxRatePercent: service.taxRatePercent,
      taxIncluded: service.taxIncluded,
      itemDiscount: line.itemDiscount,
      hsnCode: service.hsnCode ?? null,
      lineType: 'service',
      serviceId: service.serviceId,
    };
  }

  private buildSnapshotPricingLine(
    line: OfflineSalePricingLineInput,
    batch: OfflineSellableBatchSnapshot,
  ): OfflineSalePricingLineInput {
    return {
      clientLineId: line.clientLineId,
      inventoryBatchId: line.inventoryBatchId,
      itemId: batch.itemId,
      barcode: batch.barcode,
      itemName: batch.itemName,
      batchNumber: batch.batchNumber,
      quantity: line.quantity,
      salesPrice: batch.salesPrice,
      mrp: batch.mrp,
      costPrice: batch.costPrice,
      taxRatePercent: batch.taxRatePercent,
      taxIncluded: batch.taxIncluded,
      itemDiscount: line.itemDiscount,
      hsnCode: batch.hsnCode ?? null,
    };
  }
}

/**
 * Freezes on each goods line the unit its document prints, from the snapshot like every other catalog value of the
 * line; a batch from a snapshot taken before units prints NOS.
 */
function withDocumentUnits(
  pricing: OfflineFrozenSalePricing,
  byBatchId: ReadonlyMap<string, OfflineSellableBatchSnapshot>,
): OfflineFrozenSalePricing {
  return {
    ...pricing,
    lines: pricing.lines.map((line) =>
      line.lineType === 'service'
        ? line
        : { ...line, documentUnit: byBatchId.get(line.inventoryBatchId)?.documentUnit ?? 'NOS' },
    ),
  };
}
