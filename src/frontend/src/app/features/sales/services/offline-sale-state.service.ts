import type { SellabilityDecision } from '../../expiry/services/expiry.models';
import { evaluateExpiry, shopBusinessDate } from '../../expiry/services/expiry-policy-evaluator';
import { hasOfflineExpiryAuthority, isValidShopZone } from '../utils/offline-sales-snapshot.schema';
import { TillContextService } from '../../tills/services/till-context.service';
import { MONEY_SCALE, toScaled } from './offline-money.math';
import {
  discountCapacityPaise,
  discountCeilingFromPaise,
  evaluateOfflineSaleEconomics,
} from './offline-sale-economics';
import { Injectable, inject, signal } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import {
  OfflineSalesDeviceSettings,
  OfflineSalesDeviceSettingsStorage,
} from '../../../core/storage/offline-sales-device-settings.storage';
import {
  OfflineCustomerLiteSnapshot,
  OfflineSalesSnapshotIndexedDbService,
  OfflineSellableBatchSnapshot,
  OfflineSellableServiceSnapshot,
} from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { CartItem } from './sale-cart-state.service';
import { OfflineFinalizeRequest } from './offline-sale-finalization.service';
import { calculateOfflineFrozenSale } from './offline-sale-pricing-calculator';
import { OfflineSalePricingInput, OfflineSalePricingLineInput } from './offline-sale-core.types';
import { OfflineSaleFinalizationService } from './offline-sale-finalization.service';
import { OfflineSalesQueueSyncService } from './offline-sales-queue-sync.service';
import { SalePreviewDto } from './sale.models';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { invoiceErrorKey } from '../utils/invoice-error-keys';
import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';

const MAX_OFFLINE_AGE_MS = 48 * 60 * 60 * 1000;

/** Expired authority needs a catalog refresh even when its captured policy would allow the lot. */
function offlineSellability(
  decision: SellabilityDecision,
  evidenceExpiresAt: string,
): SellabilityDecision {
  if (Date.now() > Date.parse(evidenceExpiresAt)) {
    return {
      ...decision,
      allowed: false,
      availableQuantity: 0,
      reasonCode: 'Expiry.OfflineRefreshRequired',
      warning: false,
      requiresApproval: false,
    };
  }
  return decision.requiresApproval
    ? { ...decision, allowed: false, availableQuantity: 0 }
    : decision;
}

export interface OfflineSubmitRequest {
  readonly paymentMethod: number;
  readonly paidAmount: number;
  readonly dueAmount: number;
  readonly totalAmount: number;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly selectedCustomerId: string | null;
  readonly saleDiscountType: 0 | 1 | 2;
  readonly saleDiscountValue: number;
  readonly lines: readonly OfflineSalePricingLineInput[];
}

export interface OfflineSaleSubmitSuccess {
  readonly ok: true;
  readonly confirmation: {
    readonly invoiceNumber: string;
    readonly grandTotal: number;
    readonly clientSaleId: string;
  };
}

export interface OfflineSaleSubmitFailure {
  readonly ok: false;
  readonly errorKey: string;
}

export type OfflineSaleSubmitResult = OfflineSaleSubmitSuccess | OfflineSaleSubmitFailure;

interface OfflineSalePreviewContext {
  readonly paymentMethod: number;
  readonly paidAmount: number;
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly saleDiscountType: 0 | 1 | 2;
  readonly saleDiscountValue: number;
}

@Injectable({ providedIn: 'root' })
export class OfflineSaleStateService {
  private readonly tillContext = inject(TillContextService);
  private readonly authService = inject(AuthService);
  private readonly deviceSettingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly snapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly queueSync = inject(OfflineSalesQueueSyncService);
  private readonly offlineFinalization = inject(OfflineSaleFinalizationService);
  private readonly leaseDb = inject(InvoiceLeaseIndexedDbService);

  readonly offlineDeviceSettings = signal<OfflineSalesDeviceSettings | null>(null);

  readonly snapshotCompletedAt = signal<string | null>(null);
  readonly offlineCatalog = signal<readonly OfflineSellableBatchSnapshot[] | null>(null);
  readonly offlineServices = signal<readonly OfflineSellableServiceSnapshot[] | null>(null);
  readonly offlineCustomers = signal<readonly OfflineCustomerLiteSnapshot[]>([]);
  readonly offlinePendingCount = signal<number>(0);
  readonly offlineNeedsReviewCount = signal<number>(0);
  readonly offlineInvoiceRemaining = signal<number>(0);
  /**
   * The device's stored lease froze no seller profile (it was reserved before devices issued documents), so offline
   * sales will be refused until an owner or manager turns offline billing on again while online.
   */
  readonly offlineLeaseNeedsRenewal = signal(false);

  private readonly offlineCatalogCacheKey = signal<string | null>(null);

  async refreshSnapshot(): Promise<void> {
    const shopId = this.activeShopId;
    if (!shopId) {
      this.offlineDeviceSettings.set(null);
      this.snapshotCompletedAt.set(null);
      this.offlineCatalog.set(null);
      this.offlineServices.set(null);
      this.offlineCatalogCacheKey.set(null);
      this.offlineCustomers.set([]);
      this.offlinePendingCount.set(0);
      this.offlineNeedsReviewCount.set(0);
      this.offlineInvoiceRemaining.set(0);
      this.offlineLeaseNeedsRenewal.set(false);
      return;
    }

    const settings = this.deviceSettingsStorage.loadSettings(shopId);
    this.offlineDeviceSettings.set(settings);
    this.offlineCatalog.set(null);
    this.offlineServices.set(null);
    this.offlineCatalogCacheKey.set(null);
    this.snapshotCompletedAt.set(null);
    this.offlineCustomers.set([]);
    this.offlinePendingCount.set(0);
    this.offlineNeedsReviewCount.set(0);
    this.offlineInvoiceRemaining.set(settings?.lastReservedLease?.remainingCount ?? 0);
    this.offlineLeaseNeedsRenewal.set(false);

    if (!settings?.enabled || !settings.deviceId) {
      return;
    }

    if (settings.lastReservedLease) {
      try {
        const lease = await this.leaseDb.loadLease(
          shopId,
          settings.deviceId,
          settings.lastReservedLease.fiscalYear,
        );
        this.offlineLeaseNeedsRenewal.set(!!lease && !lease.sellerProfile);
      } catch {
        // non-fatal: finalization refuses such a lease on its own
      }
    }

    const snapshotInfo = await this.snapshotDb.getUsableSnapshotInfo(shopId);
    this.snapshotCompletedAt.set(snapshotInfo?.completedAt ?? null);

    try {
      const customers = await this.snapshotDb.getUsableCustomers(shopId);
      this.offlineCustomers.set(customers);
    } catch {
      // non-fatal
    }

    try {
      const visible = await this.queueSync.refreshActiveStatusCounts();
      this.offlinePendingCount.set(visible.pending + visible.syncing);
      this.offlineNeedsReviewCount.set(visible.needsReview);
    } catch {
      // non-fatal
    }
  }

  async searchOfflineCatalog(term: string): Promise<readonly AvailableBatchDto[]> {
    const shopId = this.activeShopId;
    const q = term.trim().toLowerCase();
    const metadata = shopId ? await this.snapshotDb.getUsableSnapshotInfo(shopId) : null;
    if (metadata?.schemaVersion !== 4 || !isValidShopZone(metadata.businessTimeZoneId) || !metadata.catalogVersion) throw new Error('expiry.offline.refresh');
    const businessDate = shopBusinessDate(Date.now(), metadata.businessTimeZoneId);
    const cacheKey = `${shopId}:${this.snapshotCompletedAt() ?? ''}`;

    let catalog = this.offlineCatalog();
    if (!shopId) {
      this.offlineCatalog.set(null);
      this.offlineServices.set(null);
      this.offlineCatalogCacheKey.set(null);
      return [];
    }

    if (!catalog || this.offlineCatalogCacheKey() !== cacheKey) {
      [catalog] = await Promise.all([
        this.snapshotDb.getUsableBatches(shopId),
        this.snapshotDb.getUsableServices(shopId).then((svcs) => this.offlineServices.set(svcs)),
      ]);
      this.offlineCatalog.set(catalog);
      this.offlineCatalogCacheKey.set(cacheKey);
    }

    const services = this.offlineServices() ?? [];

    const matchedBatches = catalog.filter(
      (b) => b.itemName.toLowerCase().includes(q) || b.barcode.toLowerCase().startsWith(q),
    );
    const matchedServices = services.filter(
      (s) => s.name.toLowerCase().includes(q) || (s.code ?? '').toLowerCase().startsWith(q),
    );

    const batchResults: AvailableBatchDto[] = matchedBatches.filter(b => hasOfflineExpiryAuthority(b, metadata.catalogVersion!)).map((b) => {
      const decision = evaluateExpiry({ batchId: b.batchId, itemId: b.itemId, expiryDate: b.expiryDate, manufacturingDate: b.manufacturingDate,
        physicalQuantity: b.physicalQuantity!, quarantinedQuantity: b.quarantinedQuantity!, batchVersion: b.batchVersion!, itemActive: true, isVoided: false }, b.effectivePolicy!, metadata.catalogVersion!, businessDate);
      const sellability = offlineSellability(decision, b.expiryEvidenceExpiresAt!);
      return {
      sellability, availableQuantity: sellability.availableQuantity, quarantinedQuantity: b.quarantinedQuantity, version: b.batchVersion,
      barcode: b.barcode,
      itemName: b.itemName,
      batchNumber: b.batchNumber,
      inventoryBatchId: b.batchId,
      quantity: b.physicalQuantity!,
      costPrice: b.costPrice,
      salesPrice: b.salesPrice,
      mrp: b.mrp,
      taxRatePercent: b.taxRatePercent,
      taxIncluded: b.taxIncluded,
      purchaseTaxIncluded: b.purchaseTaxIncluded,
      hsnCode: b.hsnCode ?? null,
      expiryDate: b.expiryDate ?? null,
      lineType: 'goods',
      uom: b.uom ?? '',
      // A snapshot taken before units carries no precision: whole units, printed NOS, until it is refreshed.
      quantityScale: b.quantityScale ?? 0,
      documentUnit: b.documentUnit ?? 'NOS',
    }; });

    const serviceResults: AvailableBatchDto[] = matchedServices.map((s) => ({
      barcode: s.code ?? s.serviceId,
      itemName: s.name,
      batchNumber: '',
      inventoryBatchId: s.serviceId,
      quantity: Number.MAX_SAFE_INTEGER,
      costPrice: 0,
      salesPrice: s.price,
      mrp: 0,
      taxRatePercent: s.taxRatePercent,
      taxIncluded: s.taxIncluded,
      hsnCode: s.hsnCode ?? null,
      expiryDate: null,
      lineType: 'service',
      serviceId: s.serviceId,
    }));

    return [...batchResults, ...serviceResults];
  }

  async buildOfflinePreview(
    cart: readonly CartItem[],
    context: OfflineSalePreviewContext,
  ): Promise<SalePreviewDto> {
    const metadata = await this.snapshotDb.getUsableSnapshotInfo(this.activeShopId);
    if (metadata?.schemaVersion !== 4 || !isValidShopZone(metadata.businessTimeZoneId) || !metadata.catalogVersion) throw new Error('expiry.offline.refresh');
    const businessDate = shopBusinessDate(Date.now(), metadata.businessTimeZoneId);
    const batches = await this.snapshotDb.getUsableBatches(this.activeShopId);
    const expiryDecisions = cart.map(line => {
      const batch = batches.find(x => x.batchId === line.inventoryBatchId);
      if (!batch || !hasOfflineExpiryAuthority(batch, metadata.catalogVersion!)) throw new Error('expiry.offline.refresh');
      const decision = evaluateExpiry({ batchId: batch.batchId, itemId: batch.itemId, expiryDate: batch.expiryDate, manufacturingDate: batch.manufacturingDate,
        physicalQuantity: batch.physicalQuantity!, quarantinedQuantity: batch.quarantinedQuantity!, batchVersion: batch.batchVersion!, itemActive: true, isVoided: false }, batch.effectivePolicy!, metadata.catalogVersion!, businessDate);
      return { itemId: batch.itemId, inventoryBatchId: batch.batchId, quantity: line.quantity, batchVersion: batch.batchVersion!,
        decision: offlineSellability(decision, batch.expiryEvidenceExpiresAt!) };
    });
    const rules = await this.snapshotDb.getUsableDiscountRules(this.activeShopId);
    const pricing = calculateOfflineFrozenSale({
      soldAt: new Date().toISOString(),
      paymentMethod: context.paymentMethod,
      paidAmount: context.paidAmount,
      customerId: context.customerId,
      customerName: context.customerName,
      customerPhone: context.customerPhone,
      saleDiscount: {
        type: context.saleDiscountType,
        value: context.saleDiscountValue,
      },
      lines: cart.map((item) => ({
        clientLineId: item.clientLineKey,
        inventoryBatchId: item.inventoryBatchId,
        itemId: item.inventoryBatchId,
        barcode: item.barcode,
        itemName: item.itemName,
        batchNumber: item.batchNumber,
        quantity: item.quantity,
        salesPrice: item.salesPrice,
        mrp: item.mrp,
        costPrice: item.costPrice,
        taxRatePercent: item.taxRatePercent,
        taxIncluded: item.taxIncluded,
        itemDiscount: {
          type: item.itemDiscountType as 0 | 1 | 2,
          value: item.itemDiscountValue,
        },
        hsnCode: item.hsnCode ?? null,
      })),
      rules: rules,
    });

    const configuredRuleByBatchId = new Map(
      rules
        .filter(
          (rule) =>
            rule.ruleType === 'BatchPercentage' && rule.inventoryBatchId && rule.percentage > 0,
        )
        .map((rule) => [rule.inventoryBatchId!, rule]),
    );
    const saleLevelEligibleSubtotal = this.roundAmount(
      pricing.lines.reduce((sum, line) => sum + (line.preTaxAmount - line.itemDiscountAmount), 0),
    );

    return {
      expiryDecisions,
      totalAmount: pricing.totals.grandTotal,
      totalTaxableAmount: this.roundAmount(
        pricing.lines.reduce((sum, line) => sum + line.taxableAmount, 0),
      ),
      totalTaxAmount: pricing.totals.totalTax,
      totalDiscountAmount: pricing.totals.totalDiscount,
      saleLevelEligibleSubtotal,
      configuredSaleRule: null,
      lines: pricing.lines.map((line) => {
        const configuredRule = configuredRuleByBatchId.get(line.inventoryBatchId) ?? null;
        const { maxFlat: maxAllowedItemDiscountFlat, maxPercent: maxAllowedItemDiscountPercent } =
          discountCeilingFromPaise(
            discountCapacityPaise(line.preTaxAmount, line.quantity, line.costPrice),
            toScaled(line.preTaxAmount, MONEY_SCALE),
          );

        return {
          economics: evaluateOfflineSaleEconomics({ ...pricing, lines: [line] }).lines[0],
          itemId: line.itemId,
          barcode: line.barcode,
          itemName: line.itemName,
          inventoryBatchId: line.inventoryBatchId,
          batchNumber: line.batchNumber,
          quantity: line.quantity,
          costPrice: line.costPrice,
          salesPrice: line.salesPrice,
          mrp: line.mrp,
          taxRatePercent: line.taxRatePercent,
          isPriceIncludingTax: line.taxIncluded,
          preTaxAmountBeforeDiscount: line.preTaxAmount,
          itemDiscountAmount: line.itemDiscountAmount,
          saleDiscountAmount: line.saleDiscountAmount,
          taxableAmount: line.taxableAmount,
          taxAmount: line.taxAmount,
          lineTotalAmount: line.lineTotal,
          maxAllowedItemDiscountFlat,
          maxAllowedItemDiscountPercent,
          configuredBatchRuleId: configuredRule?.ruleId ?? null,
          configuredBatchRulePercentage: configuredRule?.percentage ?? null,
          hasClientPriceMismatch: false,
          clientLineKey: line.clientLineId,
        };
      }),
      infos: [],
      warnings: [],
    };
  }

  async submitOfflineSale(payload: OfflineSubmitRequest): Promise<OfflineSaleSubmitResult> {
    const shopId = this.activeShopId;
    if (!shopId) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockDeviceNotEnabled' };
    }

    const settings = this.resolveOfflineSettings(shopId);
    if (!settings?.enabled || !settings.deviceId) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockDeviceNotEnabled' };
    }

    const issuerAtStart = this.authService.session()?.user?.id ?? null;
    let tillEvidence;
    try {
      tillEvidence = await this.tillContext.offlineEvidence(
        payload.paymentMethod,
        payload.paidAmount,
      );
    } catch {
      return { ok: false, errorKey: 'tills.sessionRequired' } as const;
    }
    const snapshotCompletedAt = await this.resolveOfflineSnapshotCompletedAt(shopId);
    if (!snapshotCompletedAt) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockSnapshotStale' };
    }

    const snapshotAgeMs = Date.now() - Date.parse(snapshotCompletedAt);
    if (!Number.isFinite(snapshotAgeMs)) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockSnapshotStale' };
    }
    if (snapshotAgeMs > MAX_OFFLINE_AGE_MS) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockSnapshotTooOld' };
    }

    const lease = this.acquireInvoiceLease();
    if (!lease) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockInvoiceUnavailable' };
    }

    const hasGrace = await this.authService.canUseOfflineSalesAuthGrace();
    if (!hasGrace) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockAuthGraceInvalid' };
    }

    if (
      !Number.isFinite(payload.paidAmount) ||
      !Number.isFinite(payload.dueAmount) ||
      payload.paidAmount < 0 ||
      payload.dueAmount < 0 ||
      !this.areAmountsEqual(payload.paidAmount + payload.dueAmount, payload.totalAmount)
    ) {
      return { ok: false, errorKey: 'sales.newSale.invalidPaymentSplit' };
    }

    const offlineCustomer = await this.resolveOfflineCustomer(shopId, payload.selectedCustomerId);
    if ((payload.paymentMethod === 4 || payload.dueAmount > 0) && !offlineCustomer) {
      return { ok: false, errorKey: 'sales.newSale.offline.blockDueRequiresCustomer' };
    }

    const pricingInput: OfflineSalePricingInput = {
      soldAt: new Date().toISOString(),
      paymentMethod: payload.paymentMethod,
      paidAmount: payload.paidAmount,
      customerId: offlineCustomer?.customerId ?? payload.customerId,
      customerName: offlineCustomer?.name ?? payload.customerName,
      customerPhone: offlineCustomer?.phoneNumber ?? payload.customerPhone,
      saleDiscount: {
        type: payload.saleDiscountType as 0 | 1 | 2,
        value: payload.saleDiscountValue,
      },
      lines: payload.lines,
      rules: [],
    };

    const request: OfflineFinalizeRequest = {
      shopId,
      deviceId: settings.deviceId,
      fiscalYear: lease.fiscalYear,
      pricingInput,
      tillEvidence,
      maxSnapshotAgeMs: MAX_OFFLINE_AGE_MS,
    };

    if (
      this.authService.session()?.activeShopId !== shopId ||
      (this.authService.session()?.user?.id ?? null) !== issuerAtStart
    )
      return { ok: false, errorKey: 'tills.capabilityUnknown' };
    const result = await this.offlineFinalization.finalizeAndQueue(request);
    if (!result.ok) {
      const reasonKey: Record<typeof result.reason, string> = {
        TILL_CONTEXT_INVALID: 'tills.sessionRequired',
        BELOW_RECORDED_COST: 'sales.newSale.offline.blockBelowRecordedCost',
        EXPIRY_REFRESH_REQUIRED: 'expiry.offline.refresh',
        EXPIRY_BLOCKED: 'expiry.offline.blocked',
        EXPIRY_ONLINE_APPROVAL_REQUIRED: 'expiry.offline.onlineApproval',
        SNAPSHOT_STALE: 'sales.newSale.offline.blockSnapshotStale',
        MISSING_CATALOG_ITEM: 'sales.newSale.offline.blockMissingItem',
        INSUFFICIENT_SHADOW_STOCK: 'sales.newSale.offline.blockInsufficientStock',
        MISSING_DUE_CUSTOMER: 'sales.newSale.offline.blockDueRequiresCustomer',
        INVOICE_UNAVAILABLE: 'sales.newSale.offline.blockInvoiceUnavailable',
        INVALID_AMOUNT: 'sales.newSale.offline.blockInvalidAmount',
        SELLER_PROFILE_UNAVAILABLE: 'sales.newSale.offline.blockSellerProfileUnavailable',
        DOCUMENT_UNAVAILABLE: 'sales.newSale.offline.blockDocumentUnavailable',
        CUSTOMER_UNAVAILABLE: 'sales.newSale.offline.blockCustomerUnavailable',
        QUANTITY_PRECISION_EXCEEDED: 'sales.newSale.offline.blockQuantityPrecision',
      };

      return {
        ok: false,
        errorKey:
          (result.reason === 'DOCUMENT_UNAVAILABLE' ? invoiceErrorKey(result.errorCode) : null) ??
          reasonKey[result.reason],
      };
    }

    this.offlineInvoiceRemaining.set(result.remainingInvoiceCount);
    await this.updateOfflineInvoiceLeaseCount(settings.shopId, result.remainingInvoiceCount);

    return {
      ok: true,
      confirmation: {
        invoiceNumber: result.payload.invoiceNumber,
        grandTotal: result.payload.pricing.totals.grandTotal,
        clientSaleId: result.payload.clientSaleId,
      },
    };
  }

  acquireInvoiceLease(): OfflineSalesDeviceSettings['lastReservedLease'] | null {
    const settings = this.offlineDeviceSettings();
    if (!settings?.enabled || !settings.lastReservedLease || !settings.deviceId) {
      return null;
    }

    const lease = settings.lastReservedLease;
    if (lease.remainingCount <= 0) {
      return null;
    }

    const expiresAtMs = Date.parse(lease.expiresAt);
    return Number.isFinite(expiresAtMs) && expiresAtMs > Date.now() ? lease : null;
  }

  private get activeShopId(): string {
    return this.authService.session()?.activeShopId ?? '';
  }

  private async updateOfflineInvoiceLeaseCount(
    shopId: string,
    remainingCount: number,
  ): Promise<void> {
    const updated = this.deviceSettingsStorage.updateSettings(shopId, (current) => ({
      ...current,
      lastReservedLease: current.lastReservedLease
        ? {
            ...current.lastReservedLease,
            remainingCount,
          }
        : null,
    }));

    if (updated) {
      this.offlineDeviceSettings.set(updated);
    }

    this.offlineInvoiceRemaining.set(remainingCount);
  }

  private async resolveOfflineSnapshotCompletedAt(shopId: string): Promise<string | null> {
    const current = this.snapshotCompletedAt();
    if (current) {
      return current;
    }

    const snapshotInfo = await this.snapshotDb.getUsableSnapshotInfo(shopId);
    const completedAt = snapshotInfo?.completedAt ?? null;
    this.snapshotCompletedAt.set(completedAt);
    return completedAt;
  }

  private resolveOfflineSettings(shopId: string): OfflineSalesDeviceSettings | null {
    const current = this.offlineDeviceSettings();
    if (current?.shopId === shopId) {
      return current;
    }

    const loaded = this.deviceSettingsStorage.loadSettings(shopId);
    this.offlineDeviceSettings.set(loaded);
    return loaded;
  }

  private async resolveOfflineCustomer(
    shopId: string,
    selectedCustomerId: string | null,
  ): Promise<OfflineCustomerLiteSnapshot | null> {
    if (!selectedCustomerId) {
      return null;
    }

    let customers = this.offlineCustomers();
    if (customers.length === 0) {
      customers = await this.snapshotDb.getUsableCustomers(shopId);
      this.offlineCustomers.set(customers);
    }

    return customers.find((customer) => customer.customerId === selectedCustomerId) ?? null;
  }

  private roundAmount(value: number): number {
    return Number(value.toFixed(2));
  }

  private areAmountsEqual(left: number, right: number): boolean {
    return this.roundAmount(left) === this.roundAmount(right);
  }
}
