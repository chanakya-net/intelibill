import { Injectable, inject } from '@angular/core';

import { InvoiceLeaseExhaustedError, InvoiceLeaseExpiredError, InvoiceLeaseIndexedDbService, InvoiceLeaseNotFoundError } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService, type OfflineSellableBatchSnapshot } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { calculateOfflineFrozenSale } from './offline-sale-pricing-calculator';
import { OfflineSalePricingInput, OfflineSalePricingLineInput, OfflineQueuedSalePayload } from './offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { OfflineSalesShadowStockIndexedDbService } from './offline-sales-shadow-stock-indexeddb.service';

export interface OfflineFinalizeRequest {
  readonly shopId: string;
  readonly deviceId: string;
  readonly fiscalYear: string;
  readonly pricingInput: OfflineSalePricingInput;
  readonly maxSnapshotAgeMs: number;
}

export type OfflineFinalizeResult =
  | { readonly ok: true; readonly payload: OfflineQueuedSalePayload; readonly remainingInvoiceCount: number }
  | { readonly ok: false; readonly reason: 'SNAPSHOT_STALE' | 'MISSING_CATALOG_ITEM' | 'INSUFFICIENT_SHADOW_STOCK' | 'MISSING_DUE_CUSTOMER' | 'INVOICE_UNAVAILABLE' };

@Injectable({ providedIn: 'root' })
export class OfflineSaleFinalizationService {
  private readonly snapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly leaseDb = inject(InvoiceLeaseIndexedDbService);
  private readonly queueDb = inject(OfflineSalesQueueIndexedDbService);
  private readonly shadowDb = inject(OfflineSalesShadowStockIndexedDbService);

  async finalizeAndQueue(request: OfflineFinalizeRequest): Promise<OfflineFinalizeResult> {
    const snapshot = await this.snapshotDb.getUsableSnapshotInfo(request.shopId);
    if (!snapshot?.completedAt) return { ok: false, reason: 'SNAPSHOT_STALE' };

    const snapshotAgeMs = Date.now() - Date.parse(snapshot.completedAt);
    if (Number.isNaN(snapshotAgeMs) || snapshotAgeMs > request.maxSnapshotAgeMs) {
      return { ok: false, reason: 'SNAPSHOT_STALE' };
    }

    const batches = await this.snapshotDb.getUsableBatches(request.shopId);
    const byBatchId = new Map(batches.map((batch) => [batch.batchId, batch]));

    const requestedByBatch = new Map<string, number>();
    for (const line of request.pricingInput.lines) {
      requestedByBatch.set(line.inventoryBatchId, (requestedByBatch.get(line.inventoryBatchId) ?? 0) + line.quantity);
    }

    for (const [inventoryBatchId, requestedQuantity] of requestedByBatch) {
      const batch = byBatchId.get(inventoryBatchId);
      if (!batch) return { ok: false, reason: 'MISSING_CATALOG_ITEM' };

      const available = await this.shadowDb.ensureQuantity(request.shopId, request.deviceId, inventoryBatchId, batch.quantity);
      if (available < requestedQuantity) return { ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' };
    }

    const pricingInputFromSnapshot: OfflineSalePricingInput = {
      ...request.pricingInput,
      lines: request.pricingInput.lines.map((line) => {
        return this.buildSnapshotPricingLine(line, byBatchId.get(line.inventoryBatchId)!);
      }),
      rules: await this.snapshotDb.getUsableDiscountRules(request.shopId),
    };

    const priced = calculateOfflineFrozenSale(pricingInputFromSnapshot);

    if (priced.totals.dueAmount > 0) {
      const customers = await this.snapshotDb.getUsableCustomers(request.shopId);
      const customerExists = !!request.pricingInput.customerId && customers.some((c) => c.customerId === request.pricingInput.customerId);
      if (!customerExists) return { ok: false, reason: 'MISSING_DUE_CUSTOMER' };
    }

    const clientSaleId = crypto.randomUUID();
    const idempotencyKey = `offline-sale-${clientSaleId}`;

    let invoiceNumber: string;
    let consumedLeaseNextNumber: number;
    let remainingInvoiceCount = 0;
    try {
      const consumed = await this.leaseDb.consumeNextInvoiceNumber(request.shopId, request.deviceId, request.fiscalYear);
      invoiceNumber = consumed.invoiceNumber;
      consumedLeaseNextNumber = consumed.lease.nextNumber;
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

    const payload: OfflineQueuedSalePayload = {
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
    };

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
      await this.leaseDb.rollbackConsumedInvoiceNumber(
        request.shopId,
        request.deviceId,
        request.fiscalYear,
        consumedLeaseNextNumber
      );
      throw error;
    }

    for (const line of request.pricingInput.lines) {
      await this.shadowDb.reduceQuantity(request.shopId, request.deviceId, line.inventoryBatchId, line.quantity);
    }

    return { ok: true, payload, remainingInvoiceCount };
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
