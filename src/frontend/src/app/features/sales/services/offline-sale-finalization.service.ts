import { evaluateExpiry, shopBusinessDate } from '../../expiry/services/expiry-policy-evaluator';
import { hasOfflineExpiryAuthority, isValidShopZone } from '../utils/offline-sales-snapshot.schema';
import type { OfflineExpiryLineEvidence } from '../../expiry/services/expiry.models';
import { Injectable, inject } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import {
  InvoiceLeaseIndexedDbService,
  type InvoiceLeaseSnapshot,
  type InvoiceNumberClaim,
} from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineIssuanceLock } from '../../../core/storage/offline-issuance-lock';
import {
  OfflineIssuanceAuthorityService,
  type OfflineIssuanceInvocation,
  type OfflineIssuanceQuestion,
  type OfflineIssuanceRefusal,
} from './offline-issuance-authority.service';
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
import { OfflineQueueWriteRefusedError, OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { OfflineSalesShadowStockIndexedDbService } from './offline-sales-shadow-stock-indexeddb.service';
import { fitsQuantityScale, normalizeQuantity } from '../../../shared/quantity/quantity-input';
import { QUANTITY_SCALE_MAX } from '../../../shared/quantity/quantity-units';

export interface OfflineFinalizeRequest {
  readonly expectedEpoch?: number;
  readonly expectedSessionId?: string | null;
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
  /** Offline permission: forged, missing or out-of-scope permit; outside its signed window; clocks not trustworthy. */
  | OfflineIssuanceRefusal
  | 'EXPIRY_REFRESH_REQUIRED'
  | 'EXPIRY_BLOCKED'
  | 'EXPIRY_ONLINE_APPROVAL_REQUIRED'
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
      /** With `OFFLINE_PERMIT_EXPIRED`: the verified, in-scope signed deadline to reconnect by. */
      readonly reconnectBy?: string;
    };

@Injectable({ providedIn: 'root' })
export class OfflineSaleFinalizationService {
  private readonly auth = inject(AuthService);
  private readonly snapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly leaseDb = inject(InvoiceLeaseIndexedDbService);
  private readonly queueDb = inject(OfflineSalesQueueIndexedDbService);
  private readonly shadowDb = inject(OfflineSalesShadowStockIndexedDbService);
  private readonly authority = inject(OfflineIssuanceAuthorityService);
  private readonly issuanceLock = inject(OfflineIssuanceLock);
  private readonly context = inject(ShopContextService);

  /**
   * Issues one offline invoice. The whole decision runs alone on this device for the shop (one exclusive lock), and
   * the stored permission is verified twice: before a number is taken and again before the queue row is written.
   */
  async finalizeAndQueue(request: OfflineFinalizeRequest): Promise<OfflineFinalizeResult> {
    // Captured before the first await: a scope that changes later is never mistaken for the one that asked.
    const invocation = this.authority.capture(request.shopId);
    const detach = new AbortController();
    const unregister = this.context.onDetach(() => detach.abort());
    try {
      const outcome = await this.issuanceLock.runExclusive(request.shopId, () =>
        this.finalizeExclusive(request, invocation, detach.signal),
      );
      // Without safe serialization two tabs could take one number; nothing is issued, other offline features go on.
      return outcome.acquired ? outcome.value : { ok: false, reason: 'INVOICE_UNAVAILABLE' };
    } finally {
      unregister();
    }
  }

  private async finalizeExclusive(
    request: OfflineFinalizeRequest,
    invocation: OfflineIssuanceInvocation | null,
    detached: AbortSignal,
  ): Promise<OfflineFinalizeResult> {
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
    if (!issuedByUserId || ownerShopId !== request.shopId) return { ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' };
    if (
      !invocation || detached.aborted || !this.authority.isCurrent(invocation) ||
      (request.expectedEpoch !== undefined && request.expectedEpoch !== invocation.epoch) ||
      (request.expectedSessionId !== undefined && request.expectedSessionId !== invocation.sessionId)
    ) {
      return { ok: false, reason: 'OFFLINE_PERMIT_INVALID' };
    }
    const snapshot = await this.snapshotDb.getUsableSnapshotInfo(request.shopId);
    if (!snapshot?.completedAt) return { ok: false, reason: 'SNAPSHOT_STALE' };

    const snapshotAgeMs = Date.now() - Date.parse(snapshot.completedAt);
    if (Number.isNaN(snapshotAgeMs) || snapshotAgeMs > request.maxSnapshotAgeMs) {
      return { ok: false, reason: 'SNAPSHOT_STALE' };
    }

    if (snapshot.schemaVersion !== 4 || !isValidShopZone(snapshot.businessTimeZoneId) || !snapshot.catalogVersion)
      return { ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' };
    // First guard: the stored permit, its signature, this owner and the clocks, before any number is taken.
    const question: OfflineIssuanceQuestion = {
      shopId: request.shopId,
      deviceId: request.deviceId,
      fiscalYear: request.fiscalYear,
      soldAt: request.pricingInput.soldAt,
      snapshotId: snapshot.snapshotId,
      snapshotCompletedAt: snapshot.completedAt,
      tillSessionId: frozenTill.till?.sessionId ?? null,
    };
    if (!(await this.settleInterruptedClaim(request))) return { ok: false, reason: 'INVOICE_UNAVAILABLE' };
    const permission = await this.authority.authorize(invocation, question);
    if (!permission.ok) return this.permissionRefusal(permission);

    const businessDate = shopBusinessDate(request.pricingInput.soldAt, snapshot.businessTimeZoneId);
    const expiryProofs = new Map<string, OfflineExpiryLineEvidence>();
    const batches = await this.snapshotDb.getUsableBatches(request.shopId);
    const byBatchId = new Map(batches.map((batch) => [batch.batchId, batch]));

    const services = await this.snapshotDb.getUsableServices(request.shopId);
    const byServiceId = new Map(services.map((svc) => [svc.serviceId, svc]));

    const requestedByBatch = new Map<string, number>();
    for (const line of request.pricingInput.lines) {
      if (line.lineType === 'service') continue;
      const batch = byBatchId.get(line.inventoryBatchId);
      if (!batch) return { ok: false, reason: 'MISSING_CATALOG_ITEM' };
      if (!hasOfflineExpiryAuthority(batch, snapshot.catalogVersion)) return { ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' };
      const issueInstant = Date.parse(request.pricingInput.soldAt);
      if (!Number.isFinite(issueInstant) || issueInstant < Date.parse(batch.expiryCapturedAt!) || issueInstant > Date.parse(batch.expiryEvidenceExpiresAt!))
        return { ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' };
      const decision = evaluateExpiry({ batchId: batch.batchId, itemId: batch.itemId, expiryDate: batch.expiryDate,
        manufacturingDate: batch.manufacturingDate, physicalQuantity: batch.physicalQuantity!, quarantinedQuantity: batch.quarantinedQuantity!,
        batchVersion: batch.batchVersion!, itemActive: true, isVoided: false }, batch.effectivePolicy!, snapshot.catalogVersion, businessDate);
      if (!decision.allowed) return { ok: false, reason: 'EXPIRY_BLOCKED', errorCode: decision.reasonCode ?? undefined };
      if (decision.requiresApproval) return { ok: false, reason: 'EXPIRY_ONLINE_APPROVAL_REQUIRED' };
      expiryProofs.set(batch.batchId, { evidenceId: batch.expiryEvidenceId!, catalogVersion: snapshot.catalogVersion,
        policyVersion: batch.effectivePolicy!.version, businessDate, expiryDate: batch.expiryDate ?? null, decisionCode: decision.reasonCode ?? 'Allowed' });
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

      const captured = batch.availableQuantity!;
      const available = await this.shadowDb.getQuantity(request.shopId, request.deviceId, inventoryBatchId);
      if (Math.min(available ?? captured, captured) < requestedQuantity) return { ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' };
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
      const withUnits = withDocumentUnits(calculateOfflineFrozenSale(pricingInputFromSnapshot), byBatchId);
      priced = { ...withUnits, lines: withUnits.lines.map(line => line.lineType === 'service' ? line : { ...line, expiryEvidence: structuredClone(expiryProofs.get(line.inventoryBatchId)!) }) };
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

    const refusal = this.authority.refusalNow(invocation, permission, question.soldAt);
    if (refusal) return this.permissionRefusal(refusal);

    let invoiceNumber: string;
    let consumedLeaseNextNumber: number;
    let consumedLease: InvoiceLeaseSnapshot;
    let remainingInvoiceCount = 0;
    try {
      const consumed = await this.leaseDb.consumeNextInvoiceNumber(request.shopId, request.deviceId, request.fiscalYear, {
        leaseId: permission.lease.leaseId,
        nextNumber: permission.lease.nextNumber,
        signedPermit: permission.signedPermit,
        clientSaleId,
      });
      invoiceNumber = consumed.invoiceNumber;
      consumedLeaseNextNumber = consumed.lease.nextNumber;
      consumedLease = consumed.lease;
      remainingInvoiceCount = consumed.remainingCount;
    } catch {
      // Not found, expired, exhausted, or no longer the lease that was verified: nothing was taken.
      return { ok: false, reason: 'INVOICE_UNAVAILABLE' };
    }
    const claim: InvoiceNumberClaim = {
      leaseId: permission.lease.leaseId,
      number: permission.lease.nextNumber,
      clientSaleId,
    };

    // The document is frozen once, here, under the identity the lease froze: the server issues its own copy from the
    // same profile on sync, and no later shop or customer edit reaches a reprint. A sale that cannot be issued gives
    // its number back before anything is handed over.
    const sellerProfile = consumedLease.sellerProfile;
    const fiscalYearStart = fiscalYearStartOf(consumedLease.fiscalYear ?? request.fiscalYear);
    if (!sellerProfile || fiscalYearStart === null) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber, claim);
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
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber, claim);
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
      licenceEvidence: permission.evidence,
    };

    if (
      (this.auth.session()?.user.id || null) !== issuedByUserId ||
      (this.auth.session()?.activeShopId ?? null) !== ownerShopId
    ) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber, claim);
      throw new Error('Offline invoice owner changed before persistence.');
    }

    // Second guard: everything is read and verified again, against the very lease the number came from.
    const current = await this.snapshotDb.getUsableSnapshotInfo(request.shopId);
    const recheck =
      current?.snapshotId === snapshot.snapshotId && !detached.aborted
        ? await this.authority.authorize(invocation, {
            ...question,
            expected: {
              leaseId: claim.leaseId,
              signedPermit: permission.signedPermit,
              nextNumber: consumedLeaseNextNumber,
            },
          })
        : ({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' } as const);
    if (!recheck.ok) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber, claim);
      return this.permissionRefusal(recheck);
    }

    let queueRefusal: ReturnType<OfflineIssuanceAuthorityService['refusalNow']> = null;
    try {
      await this.queueDb.savePendingSale(
        {
          shopId: request.shopId,
          deviceId: request.deviceId,
          clientSaleId,
          idempotencyKey,
          invoiceNumber,
          soldAt: request.pricingInput.soldAt,
          payload,
        },
        {
          // Synchronous, inside the queue write: owner, session and signed window, with no await before the insert.
          stillAllowed: () => {
            queueRefusal = this.authority.refusalNow(invocation, recheck, question.soldAt);
            return queueRefusal === null;
          },
          signal: detached,
        },
      );
    } catch (error) {
      await this.releaseInvoiceNumber(request, consumedLeaseNextNumber, claim);
      if (error instanceof OfflineQueueWriteRefusedError) {
        return this.permissionRefusal(queueRefusal ?? { reason: 'OFFLINE_PERMIT_INVALID' });
      }
      throw error;
    }

    // The row is durable: the number is issued whatever happens next, so its claim is settled before anything else.
    // A claim that cannot be cleared now is settled, cursor untouched, before the next number is taken.
    await this.leaseDb.settleClaim(request.shopId, request.deviceId, request.fiscalYear, clientSaleId).catch(() => undefined);

    for (const batchId of requestedByBatch.keys()) {
      await this.shadowDb.ensureQuantity(request.shopId, request.deviceId, batchId, byBatchId.get(batchId)!.availableQuantity!);
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

  /** Gives back exactly the number this sale took: same lease, same cursor, same claim. Anything else stays. */
  private async releaseInvoiceNumber(
    request: OfflineFinalizeRequest,
    consumedLeaseNextNumber: number,
    claim: InvoiceNumberClaim,
  ): Promise<void> {
    await this.leaseDb.rollbackConsumedInvoiceNumber(
      request.shopId,
      request.deviceId,
      request.fiscalYear,
      consumedLeaseNextNumber,
      claim,
    );
  }

  /**
   * An issuance that was cut off (tab closed, crash) leaves its claim on the lease. Whether or not its queue row made
   * it to disk, that number is never handed out again: the claim is settled with the cursor left advanced. If the
   * queue cannot be read, the outcome is unknown and nothing new is issued.
   */
  private async settleInterruptedClaim(request: OfflineFinalizeRequest): Promise<boolean> {
    try {
      const pending = await this.leaseDb.loadPendingClaim(request.shopId, request.deviceId, request.fiscalYear);
      if (!pending) return true;
      await this.queueDb.getQueuedSale(request.shopId, request.deviceId, pending.clientSaleId);
      await this.leaseDb.settleClaim(request.shopId, request.deviceId, request.fiscalYear, pending.clientSaleId);
      return true;
    } catch {
      return false;
    }
  }

  private permissionRefusal(decision: {
    readonly reason: OfflineIssuanceRefusal;
    readonly validUntil?: string;
  }): OfflineFinalizeResult {
    return decision.validUntil
      ? { ok: false, reason: decision.reason, reconnectBy: decision.validUntil }
      : { ok: false, reason: decision.reason };
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
