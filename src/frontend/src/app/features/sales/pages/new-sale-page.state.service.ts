import { of } from 'rxjs';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { Customer, CustomerService } from '../../customers/services/customer.service';
import { RemotePartySearch } from '../../../shared/models/remote-party-search';
import { customerOptionLabel } from '../../../shared/models/party-directory.models';
import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import { signal as errorSignal } from '@angular/core';
import { shopBusinessDate } from '../../expiry/services/expiry-policy-evaluator';
import { computed, DestroyRef, inject, Injectable, Injector, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
} from '../../../shared/primeng-pt.config';
import { InputValidators } from '../../../shared/forms/input-validation';
import {
  fitsQuantityScale,
  normalizeQuantity,
  quantityStep,
} from '../../../shared/quantity/quantity-input';
import { QuantityMessage, tooPreciseMessage } from '../../../shared/quantity/quantity-messages';
import { TranslocoService } from '@ngneat/transloco';
import { QUANTITY_SCALE_MAX } from '../../../shared/quantity/quantity-units';
import { batchToGoodsSellable } from '../utils/sellable-goods.mapper';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { ShopUpdatesSignalRService } from '../../../core/services/shop-updates-signalr.service';
import { CustomersFacade } from '../../customers/state/customers.facade';
import { InventoryService } from '../../inventory/services/inventory.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import {
  PAYMENT_METHOD_VALUES,
  PaymentMethod,
  SaleCreditOverride,
  SellableDto,
} from '../services/sale.models';
import type {
  AppliedCreditNote,
  CreditNoteRedemptionRequest,
  SalePreviewDto,
  SalePreviewLineDto,
  ServiceCartLine,
} from '../services/sale.models';
import {
  SaleCartStateService,
  CartItem,
  QuantityRefusal,
} from '../services/sale-cart-state.service';
import { SaleService } from '../services/sale.service';
import { CheckoutIntentService, CheckoutScope } from '../services/checkout-intent.service';
import type { CheckoutIntent } from '../../../core/storage/checkout-intent-indexeddb.service';
import { OfflineSaleStateService } from '../services/offline-sale-state.service';
import { SalePreviewService } from '../services/sale-preview.service';
import { SalesFacade } from '../state/sales.facade';
import { CustomerDto } from '../components/new-sale/sale-customer-section.component';
import { PaymentMethodOption } from '../components/new-sale/sale-payment-section.component';
import { CreateSaleResponse } from '../components/new-sale/sale-confirmation-dialog.component';
import { SyncStatus } from '../components/new-sale/offline-status-banner.component';

/** Why the cart refused a quantity, as the message the page shows. */
export const QUANTITY_REFUSAL_KEYS: Readonly<Record<QuantityRefusal, string>> = {
  exceedsStock: 'sales.newSale.exceedsStock',
  tooPrecise: 'sales.newSale.quantityPrecisionExceeded',
  notPositive: 'common.quantity.errors.notPositive',
};

/** Every message the cart may show for a refused quantity, so a change that lands can clear exactly those. */
export const QUANTITY_REFUSAL_MESSAGE_KEYS: ReadonlySet<string> = new Set([
  ...Object.values(QUANTITY_REFUSAL_KEYS),
  'common.quantity.errors.tooPrecise',
  'common.quantity.errors.tooPreciseWhole',
]);

/** A line a refusal can name: its unit label (or the item's name when it has none) and its allowed decimals. */
export interface QuantityRefusalLine {
  readonly uom?: string | null;
  readonly itemName?: string | null;
  readonly serviceName?: string | null;
  readonly quantityScale?: number | null;
}

const STALE_INTERVAL_4H = 4 * 60 * 60 * 1000;
const MAX_OFFLINE_AGE_MS = 48 * 60 * 60 * 1000;

@Injectable()
export abstract class NewSalePageStateService {
  protected readonly cartRetentionMs = 5 * 60 * 1000;
  protected readonly offlineClockIntervalMs = 60 * 1000;
  protected readonly nowTick = signal(Date.now());
  protected readonly fb = inject(FormBuilder);
  protected readonly router = inject(Router);
  protected readonly route = inject(ActivatedRoute, { optional: true });
  protected readonly authService = inject(AuthService);
  protected readonly catalogSync = inject(ProductCatalogSyncService);
  protected readonly inventoryService = inject(InventoryService);
  protected readonly partyContext = inject(ShopContextService);
  protected readonly salesFacade = inject(SalesFacade);
  protected readonly cartState = inject(SaleCartStateService);
  protected readonly salePreview = inject(SalePreviewService);
  protected readonly shopUpdatesService = inject(ShopUpdatesSignalRService);
  protected readonly destroyRef = inject(DestroyRef);
  protected readonly transloco = inject(TranslocoService);
  protected readonly injector = inject(Injector);
  protected readonly networkStatus = inject(NetworkStatusService);
  protected readonly offlineSaleState = inject(OfflineSaleStateService);
  protected readonly saleService = inject(SaleService);
  protected readonly checkoutIntents = inject(CheckoutIntentService);

  readonly paymentMethods = PAYMENT_METHOD_VALUES;
  readonly cart = this.cartState.cart;
  readonly serviceCart = this.cartState.serviceCart;
  readonly hasGoodsLines = computed(() => this.cart().length > 0);
  readonly hasServiceLines = computed(() => this.serviceCart().length > 0);
  readonly isMixedCart = computed(() => this.hasGoodsLines() && this.hasServiceLines());
  readonly searchInput = signal('');
  readonly searchSuggestions = signal<string[]>([]);
  readonly isSearchingBatches = signal(false);
  readonly batchSearchError = signal('');
  /** What the message in `batchSearchError` interpolates (the unit and its allowed decimals), when it names a unit. */
  readonly batchSearchErrorParams = signal<QuantityMessage['params'] | null>(null);
  readonly availableBatches = signal<readonly AvailableBatchDto[]>([]);
  readonly availableSellables = signal<readonly SellableDto[]>([]);
  readonly pickerSellables = computed<readonly SellableDto[]>(() => {
    const sellables = this.availableSellables();
    if (sellables.length > 0) {
      return sellables;
    }
    return this.availableBatches().map(batchToGoodsSellable);
  });
  readonly quickProductTiles = computed(() => {
    const seenKeys = new Set<string>();
    const tiles: AvailableBatchDto[] = [];

    for (const batch of this.availableBatches()) {
      const key = `${batch.barcode}|${batch.itemName}`;
      if (seenKeys.has(key)) {
        continue;
      }

      seenKeys.add(key);
      tiles.push(batch);

      if (tiles.length >= 4) {
        break;
      }
    }

    return tiles;
  });
  readonly showBatchPicker = signal(false);
  readonly selectedSellable = signal<SellableDto | null>(null);
  readonly selectedCustomerId = signal<string | null>(null);
  readonly selectedCustomerName = signal<string | null>(null);
  readonly customerNameSuggestions = signal<string[]>([]);
  readonly isScannerOpen = signal(false);
  readonly isWalkIn = signal(true);
  readonly paymentSplitError = signal('');
  readonly lastEditedPaymentField = signal<'paid' | 'due'>('due');
  protected isSyncingPaymentControls = false;
  readonly paymentMethodsForInput: readonly PaymentMethodOption[] = PAYMENT_METHOD_VALUES;
  readonly selectedCustomer = signal<CustomerDto | null>(null);
  /** An owner's or manager's approval to exceed the selected customer's enforced credit limit. */
  readonly creditOverride = signal<SaleCreditOverride | null>(null);
  readonly creditNoteCustomerMismatchWarning = signal(false);
  readonly creditNoteCustomerMismatchConfirmed = signal(false);
  readonly routeCustomerId = (this.route?.snapshot.queryParamMap.get('customerId') ?? '').trim();
  readonly customerSelectionPool = computed<readonly CustomerDto[]>(() => {
    if (this.isOfflineMode()) {
      return this.offlineCustomers().map((customer) => ({
        customerId: customer.customerId,
        name: customer.name,
        phoneNumber: customer.phoneNumber,
        address: null,
      }));
    }

    return this.customers()
      .filter((customer) => customer.isActive)
      .map((customer) => ({
        customerId: customer.customerId,
        name: customer.name,
        phoneNumber: customer.phoneNumber,
        address: customer.address,
      }));
  });
  readonly syncStatus = computed<SyncStatus>(() => ({
    snapshotAgeHours: this.snapshotAgeHours(),
    offlineInvoiceRemaining: this.offlineInvoiceRemaining(),
    pendingSyncCount: this.offlinePendingCount(),
    needsReviewCount: this.offlineNeedsReviewCount(),
    staleWarningCount: this.staleWarningCount(),
    isSnapshotTooOld: this.isSnapshotTooOld(),
    isOfflineEligible: this.isOfflineEligible(),
  }));

  readonly expiryApprovalReason = this.salePreview.expiryApprovalReason;
  readonly expiryBusinessTimeZone = signal<string | null>(null);
  private expiryReasonCart = '';
  private expiryReasonPreview: SalePreviewDto | null = null;
  readonly expiryApprovalLines = computed(
    () => this.checkoutPreview()?.expiryDecisions?.filter((x) => x.decision.requiresApproval) ?? [],
  );
  onExpiryApprovalReason(value: string): void {
    this.expiryApprovalReason.set(value);
    this.expiryReasonCart = JSON.stringify(this.cart());
    this.expiryReasonPreview = this.checkoutPreview();
  }
  expiryLotLabel(batchId: string): string {
    const line = this.cart().find((x) => x.inventoryBatchId === batchId);
    return line ? `${line.itemName} · ${line.batchNumber}` : '';
  }
  invalidateExpiryCatalog(): void {
    this.availableBatches.set([]);
    this.availableSellables.set([]);
    this.selectedSellable.set(null);
    this.salePreview.triggerServerUpdateRefresh();
  }
  expiryReasonIsCurrent(): boolean {
    return (
      this.expiryReasonPreview === this.checkoutPreview() &&
      this.expiryReasonCart === JSON.stringify(this.cart())
    );
  }
  expiryPreviewIsCurrent(): boolean {
    const decisions = this.checkoutPreview()?.expiryDecisions;
    if (!decisions?.length) return true;
    const zone = this.expiryBusinessTimeZone();
    return (
      !!zone &&
      decisions.every((x) => x.decision.businessDate === shopBusinessDate(Date.now(), zone))
    );
  }

  readonly checkoutPreview = this.salePreview.checkoutPreview;
  readonly isPreviewLoading = this.salePreview.isPreviewLoading;
  readonly previewError = this.salePreview.previewError;

  // Discounts (cashier instant override). Configured discounts come from preview DTO.
  readonly isSaleDiscountEditorOpen = signal(false);
  readonly saleDiscountType = signal<0 | 1 | 2>(0);
  readonly saleDiscountValue = signal(0);
  readonly saleDiscountError = signal('');

  readonly openLineDiscountEditorByKey = signal<Record<string, boolean>>({});
  readonly cartItemDiscountErrorByKey = signal<Record<string, string>>({});
  readonly cartItemHsnErrorByKey = signal<Record<string, string>>({});
  readonly cartItemTaxErrorByKey = signal<Record<string, string>>({});

  // Shop realtime updates
  readonly highlightedRowKeys = signal<Set<string>>(new Set());
  readonly showUpdateNotification = signal(false);
  readonly updateNotificationText = signal('');
  readonly showConfirmation = signal(false);

  readonly isSubmitting = this.salesFacade.submitting;
  readonly serverError = this.salesFacade.errorMessage;
  readonly localizedError =
    this.salesFacade.localizedError ?? errorSignal<LocalizedError | undefined>(undefined);
  readonly lastMutationSucceeded = this.salesFacade.lastMutationSucceeded;
  readonly lastMutationType = this.salesFacade.lastMutationType;
  readonly lastRecordedSale = this.salesFacade.lastRecordedSale;
  protected readonly onlineCustomerSearch = new RemotePartySearch<Customer>(
    (search) =>
      this.isOfflineMode()
        ? of({ items: [] })
        : this.injector
            .get(CustomerService)
            .getDirectory({ search, status: 'active', pageNumber: 1, pageSize: 20 }),
    () => this.partyContext.epoch(),
    (items) => {
      if (!this.isOfflineMode()) this.customerNameSuggestions.set(items.map(customerOptionLabel));
    },
  );
  readonly customers = this.onlineCustomerSearch.items;
  readonly customerSearchLoading = this.onlineCustomerSearch.loading;
  readonly customerSearchError = this.onlineCustomerSearch.error;
  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly lastRecordedIntentId = this.salesFacade.lastRecordedIntentId;
  readonly checkoutScope = computed<CheckoutScope>(() => ({
    userId: this.authService.session()?.user?.id ?? '',
    shopId: this.activeShopId(),
  }));
  /** Intent frozen from the current cart; while unresolved, the cart cannot become another sale. */
  readonly activeCheckoutIntentId = signal<string | null>(null);
  /** Unresolved checkouts in the current user/shop scope, oldest first. */
  readonly pendingCheckouts = signal<readonly CheckoutIntent[]>([]);
  readonly activeCheckoutIntent = computed(() => {
    const intentId = this.activeCheckoutIntentId();
    return intentId
      ? (this.pendingCheckouts().find((intent) => intent.intentId === intentId) ?? null)
      : null;
  });
  readonly isReconcilingCheckout = signal(false);
  readonly cartBootstrapped = this.cartState.cartBootstrapped;
  readonly draftSaveFailed = this.cartState.draftSaveFailed;

  // Offline mode signals
  readonly deviceSettings = this.offlineSaleState.offlineDeviceSettings;
  readonly snapshotCompletedAt = this.offlineSaleState.snapshotCompletedAt;
  readonly offlinePendingCount = this.offlineSaleState.offlinePendingCount;
  readonly offlineNeedsReviewCount = this.offlineSaleState.offlineNeedsReviewCount;
  readonly offlineInvoiceRemaining = this.offlineSaleState.offlineInvoiceRemaining;
  readonly offlineLeaseNeedsRenewal = this.offlineSaleState.offlineLeaseNeedsRenewal;
  readonly offlineCatalog = this.offlineSaleState.offlineCatalog;
  readonly offlineCustomers = this.offlineSaleState.offlineCustomers;
  readonly isOfflineSubmitting = signal(false);
  readonly offlineConfirmation = signal<{
    invoiceNumber: string;
    grandTotal: number;
    clientSaleId: string;
  } | null>(null);
  readonly showOfflineConfirmation = signal(false);
  /** The offline invoice's print view when the browser blocked its window; the confirmation offers it as a link. */
  readonly blockedOfflinePrintUrl = signal<string | null>(null);

  readonly subtotalAmount = computed(() => {
    const preview = this.checkoutPreview();
    if (preview !== null) {
      return preview.totalAmount - preview.totalTaxAmount;
    }
    const goodsSubtotal = this.cart().reduce((sum, item) => sum + this.getLineSubtotal(item), 0);
    const serviceSubtotal = this.serviceCart().reduce(
      (sum, svc) => sum + this.cartState.getServiceLineSubtotal(svc),
      0,
    );
    return goodsSubtotal + serviceSubtotal;
  });
  readonly totalTaxAmount = computed(() => {
    const preview = this.checkoutPreview();
    if (preview !== null) {
      return preview.totalTaxAmount;
    }
    const goodsTax = this.cart().reduce((sum, item) => sum + this.getLineTaxAmount(item), 0);
    const serviceTax = this.serviceCart().reduce(
      (sum, svc) => sum + this.cartState.getServiceLineTaxAmount(svc),
      0,
    );
    return goodsTax + serviceTax;
  });
  readonly totalAmount = computed(() => {
    const preview = this.checkoutPreview();
    if (preview !== null) {
      return preview.totalAmount;
    }
    const goodsTotal = this.cart().reduce((sum, item) => sum + this.getLineTotal(item), 0);
    const serviceTotal = this.serviceCart().reduce(
      (sum, svc) => sum + this.cartState.getServiceLineTotal(svc),
      0,
    );
    return goodsTotal + serviceTotal;
  });

  readonly saleConfirmationResult = computed<CreateSaleResponse | null>(() => {
    const sale = this.lastRecordedSale();
    if (!sale) {
      return null;
    }

    return {
      saleId: sale.saleId,
      invoiceNumber: sale.invoiceNumber,
      totalAmount: sale.totalAmount,
      expiryDecisions: sale.expiryDecisions,
      isOffline: false,
      printTemplate: 'a4',
    };
  });

  readonly offlineSaleConfirmationResult = computed<CreateSaleResponse | null>(() => {
    const confirmation = this.offlineConfirmation();
    if (!confirmation) {
      return null;
    }

    return {
      saleId: confirmation.clientSaleId,
      invoiceNumber: confirmation.invoiceNumber,
      totalAmount: confirmation.grandTotal,
      isOffline: true,
      printTemplate: 'a4',
    };
  });

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  readonly instantDiscountTypeOptions: { value: 0 | 1 | 2; labelKey: string }[] = [
    { value: 0, labelKey: 'sales.newSale.discounts.none' },
    { value: 1, labelKey: 'sales.newSale.discounts.percent' },
    { value: 2, labelKey: 'sales.newSale.discounts.flat' },
  ];

  readonly canUseCredit = computed(() => !!this.selectedCustomerId());
  /** Owners and managers may approve going over an enforced credit limit; the server checks again. */
  readonly canOverrideCredit = computed(() => {
    const session = this.authService.session();
    const role = session?.shops?.find((shop) => shop.shopId === session.activeShopId)?.role;
    return role === 'Owner' || role === 'Manager';
  });
  readonly canSelectCreditPaymentMethod = computed(
    () => this.isOfflineMode() || this.canUseCredit(),
  );
  readonly paymentMethodsForSelection = computed(() =>
    this.paymentMethods.filter(
      (method) => method.value !== 4 || this.canSelectCreditPaymentMethod(),
    ),
  );
  readonly selectedPaymentMethod = computed(() =>
    this.getPaymentMethodLabel(this.paymentForm.controls.paymentMethod.value),
  );
  readonly loadingCustomers = computed(() => this.onlineCustomerSearch.loading());

  readonly totalDiscountAmount = computed(() => this.checkoutPreview()?.totalDiscountAmount ?? 0);
  readonly appliedCreditNotes = signal<AppliedCreditNote[]>([]);
  readonly canApplyCreditNote = computed(() => false);
  readonly totalAppliedCreditNoteAmount = computed(() =>
    this.appliedCreditNotes().reduce((sum, note) => sum + note.amount, 0),
  );
  readonly batchPickerQuantity = signal(1);

  readonly isLineDiscountEditorOpenFn = (itemId: string): boolean =>
    this.isLineDiscountEditorOpen(itemId);
  readonly hasTaxFn = (item: CartItem): boolean => this.hasTax(item);
  readonly canIncreaseFn = (item: CartItem): boolean => this.canIncreaseCartItem(item);
  readonly getLineSubtotalFn = (item: CartItem): number => this.getLineSubtotal(item);
  readonly getLineTaxAmountFn = (item: CartItem): number => this.getLineTaxAmount(item);
  readonly getLineTotalFn = (item: CartItem): number => this.getLineTotal(item);
  readonly getUnitSubtotalFn = (item: CartItem): number => this.getUnitSubtotal(item);
  readonly getUnitTaxAmountFn = (item: CartItem): number => this.getUnitTaxAmount(item);
  readonly getUnitFinalPriceFn = (item: CartItem): number => this.getUnitFinalPrice(item);
  readonly getServiceLineTotalFn = (item: ServiceCartLine): number =>
    this.cartState.getServiceLineTotal(item);
  readonly getPreviewLineFn = (itemId: string) => this.getPreviewLine(itemId);
  readonly getCartItemHsnErrorFn = (itemId: string) => this.getCartItemHsnError(itemId);
  readonly getCartItemTaxErrorFn = (itemId: string) => this.getCartItemTaxError(itemId);
  readonly getCartItemDiscountErrorFn = (itemId: string) => this.getCartItemDiscountError(itemId);

  readonly isOfflineMode = computed(() => !this.networkStatus.canReachApi());

  readonly isOfflineEligible = computed(() => {
    const settings = this.deviceSettings();
    return !!settings?.enabled;
  });

  protected snapshotAgeMs(): number | null {
    const completedAt = this.snapshotCompletedAt();
    if (!completedAt) return null;
    const nowMs = this.nowTick();
    const ms = nowMs - Date.parse(completedAt);
    return Number.isFinite(ms) ? ms : null;
  }

  staleWarningCount(): number {
    const ageMs = this.snapshotAgeMs();
    if (ageMs === null || ageMs <= 0) return 0;
    return Math.floor(ageMs / STALE_INTERVAL_4H);
  }

  isSnapshotTooOld(): boolean {
    const ageMs = this.snapshotAgeMs();
    if (ageMs === null) return false;
    return ageMs > MAX_OFFLINE_AGE_MS;
  }

  snapshotAgeHours(): number | null {
    const ageMs = this.snapshotAgeMs();
    if (ageMs === null) return null;
    return Math.floor(ageMs / (60 * 60 * 1000));
  }

  readonly canCreateNewCustomer = computed(() => !this.isOfflineMode());

  readonly batchPickerForm = this.fb.nonNullable.group({
    batchNumber: [''],
    // Whether the quantity fits the picked item's unit is checked when it is added (onAddToCart).
    quantity: [1, [Validators.required, Validators.min(quantityStep(QUANTITY_SCALE_MAX))]],
  });

  readonly customerForm = this.fb.nonNullable.group({
    customerName: ['', InputValidators.optionalText(180)],
    customerPhone: ['', InputValidators.phoneNumber({ required: false })],
  });

  readonly paymentForm = this.fb.nonNullable.group({
    paymentMethod: [1, Validators.required],
    paidAmount: [0, [Validators.required, Validators.min(0)]],
    dueAmount: [0, [Validators.required, Validators.min(0)]],
  });

  protected initialized = false;

  protected quantityRefusalKey(refusal: QuantityRefusal): string {
    return QUANTITY_REFUSAL_KEYS[refusal];
  }

  /** What is left of a batch after the quantity already in the cart for it. */
  protected remainingStock(
    batch: Pick<AvailableBatchDto, 'inventoryBatchId' | 'quantity'>,
  ): number {
    const line = this.cart().find((item) => item.inventoryBatchId === batch.inventoryBatchId);
    const available = Math.max(line?.availableQuantity ?? 0, batch.quantity);
    return normalizeQuantity(available - (line?.quantity ?? 0), QUANTITY_SCALE_MAX);
  }

  /**
   * Shows why a quantity was refused. A too-precise quantity names the line's unit and the decimals it allows;
   * without a line to name, the generic message is shown.
   */
  protected showQuantityRefusal(refusal: QuantityRefusal, line?: QuantityRefusalLine | null): void {
    if (refusal === 'tooPrecise' && line) {
      const message = tooPreciseMessage(
        line.uom || line.itemName || line.serviceName || '',
        line.quantityScale ?? 0,
        this.transloco.getActiveLang(),
      );
      this.batchSearchErrorParams.set(message.params);
      this.batchSearchError.set(message.key);
      return;
    }

    this.batchSearchErrorParams.set(null);
    this.batchSearchError.set(this.quantityRefusalKey(refusal));
  }

  /**
   * The message for the server's answer. When the server refused a quantity's precision and exactly one cart line
   * holds more decimals than its own unit allows, that line's unit is named; otherwise the generic text stays.
   */
  serverErrorMessage(): {
    readonly key: string;
    readonly params: QuantityMessage['params'] | null;
  } {
    const key = this.serverError() ?? '';
    if (key !== QUANTITY_REFUSAL_KEYS.tooPrecise) {
      return { key, params: null };
    }

    const lines: QuantityRefusalLine[] = this.cart()
      .filter((item) => !fitsQuantityScale(item.quantity, item.quantityScale))
      .map((item) => item);
    lines.push(...this.serviceCart().filter((item) => !fitsQuantityScale(item.quantity, 0)));
    if (lines.length !== 1) {
      return { key, params: null };
    }

    const named = tooPreciseMessage(
      lines[0].uom || lines[0].itemName || lines[0].serviceName || '',
      lines[0].quantityScale ?? 0,
      this.transloco.getActiveLang(),
    );
    return { key: named.key, params: named.params };
  }

  protected readFacadeValue<T>(value: T | (() => T)): T {
    return typeof value === 'function' ? (value as () => T)() : value;
  }

  onApplyVerifiedCreditNote(): void {}

  onAppliedCreditNoteAmountChange(creditNoteId: string, amount: number | null | undefined): void {
    this.appliedCreditNotes.update((notes) =>
      notes.map((note) =>
        note.creditNoteId === creditNoteId
          ? { ...note, amount: this.normalizeCreditNoteAmount(note, amount) }
          : note,
      ),
    );
    this.reconcilePaymentSplitAfterCreditNoteChange();
  }

  onRemoveAppliedCreditNote(creditNoteId: string): void {
    this.appliedCreditNotes.update((notes) =>
      notes.filter((note) => note.creditNoteId !== creditNoteId),
    );
    this.reconcilePaymentSplitAfterCreditNoteChange();
  }

  protected addAppliedCreditNote(note: AppliedCreditNote): void {
    this.appliedCreditNotes.update((notes) => [...notes, note]);
    this.reconcilePaymentSplitAfterCreditNoteChange();
  }

  protected clearAppliedCreditNotes(): void {
    this.appliedCreditNotes.set([]);
    this.reconcilePaymentSplitAfterCreditNoteChange();
  }

  protected hasAppliedCreditNoteCode(code: string): boolean {
    return this.appliedCreditNotes().some((note) => note.code === code);
  }

  protected reconcileAppliedCreditNotesAgainstCartTotal(): void {
    const totalAmount = this.totalAmount();
    if (this.appliedCreditNotes().length === 0) {
      return;
    }

    const current = this.appliedCreditNotes();
    let remaining = this.roundAmount(totalAmount);
    const normalized = current.map((note) => {
      const amount = this.roundAmount(
        Math.max(0, Math.min(note.amount, note.availableBalance, remaining)),
      );
      remaining = this.roundAmount(Math.max(0, remaining - amount));
      return { ...note, amount };
    });
    const hasChanged = normalized.some(
      (note, index) => !this.areAmountsEqual(note.amount, current[index]?.amount),
    );

    if (hasChanged) {
      this.appliedCreditNotes.set(normalized);
      this.reconcilePaymentSplitAfterCreditNoteChange();
    }
  }

  protected normalizeCreditNoteAmount(
    note: AppliedCreditNote,
    amount: number | null | undefined,
  ): number {
    const remainingPayable = this.remainingPayableAmount();
    return this.roundAmount(
      Math.max(
        0,
        Math.min(Number(amount ?? 0), note.availableBalance, remainingPayable + note.amount),
      ),
    );
  }

  remainingPayableAmount(): number {
    return this.roundAmount(Math.max(0, this.totalAmount() - this.totalAppliedCreditNoteAmount()));
  }

  protected reconcilePaymentSplitAfterCreditNoteChange(): void {
    const total = this.remainingPayableAmount();
    if (this.lastEditedPaymentField() === 'paid') {
      this.syncPaymentSplitFromPaid(this.paymentForm.controls.paidAmount.value, total);
      return;
    }
    this.syncPaymentSplitFromDue(this.paymentForm.controls.dueAmount.value, total);
  }

  abstract onAddToCart(): void;
  abstract onIncreaseCartItem(index: number): void;
  abstract onDecreaseCartItem(index: number): void;
  abstract onRemoveCartItem(index: number): void;
  abstract onRemoveServiceItem(clientLineKey: string): void;
  abstract onServiceItemPriceChange(clientLineKey: string, value: number | null | undefined): void;
  abstract onServiceItemQuantityChange(
    clientLineKey: string,
    value: number | null | undefined,
  ): void;
  abstract onQuickProductTileSelected(batch: AvailableBatchDto): void;
  abstract canIncreaseCartItem(item: CartItem): boolean;
  abstract hasTax(item: CartItem): boolean;
  abstract getLineSubtotal(item: CartItem): number;
  abstract getLineTaxAmount(item: CartItem): number;
  abstract getLineTotal(item: CartItem): number;
  abstract getUnitSubtotal(item: CartItem): number;
  abstract getUnitTaxAmount(item: CartItem): number;
  abstract getUnitFinalPrice(item: CartItem): number;
  abstract onSubmit(): Promise<void>;
  abstract createSaleIdempotencyKey(): string;
  abstract onCancel(): void;
  abstract toggleSaleDiscountEditor(): void;
  abstract onSaleDiscountTypeChange(type: 0 | 1 | 2): void;
  abstract onSaleDiscountValueChange(value: number | null | undefined): void;
  abstract isSaleDiscountEligible(): boolean;
  abstract getPreviewLine(clientLineKey: string): SalePreviewLineDto | null;
  abstract toggleLineDiscountEditor(clientLineKey: string): void;
  abstract isLineDiscountEditorOpen(clientLineKey: string): boolean;
  abstract getCartItemDiscountError(clientLineKey: string): string;
  abstract getCartItemHsnError(clientLineKey: string): string;
  abstract getCartItemTaxError(clientLineKey: string): string;
  abstract getPaymentMethodLabel(paymentMethod: number): PaymentMethod;
  abstract onCartItemDiscountTypeChange(clientLineKey: string, type: 0 | 1 | 2): void;
  abstract onCartItemDiscountValueChange(
    clientLineKey: string,
    value: number | null | undefined,
  ): void;
  abstract onCartItemHsnCodeChange(clientLineKey: string, value: string | null | undefined): void;
  abstract onCartItemTaxRateChange(clientLineKey: string, value: number | null | undefined): void;
  abstract getLineDiscountLimits(clientLineKey: string): { maxFlat: number; maxPercent: number };
  abstract getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number };
  abstract revalidateDiscountsAgainstPreview(): void;
  abstract revalidateLineOverrides(cart: readonly CartItem[]): boolean;
  abstract getBlockingCartValidationErrorKey(cart: readonly CartItem[]): string | null;
  abstract normalizeHsnCode(value: string | null | undefined): string | null;
  abstract normalizeAmount(value: number | null | undefined, total: number): number;
  abstract toFiniteAmount(value: number | null | undefined): number;
  abstract roundAmount(value: number): number;
  abstract areAmountsEqual(left: number, right: number): boolean;
  abstract syncPaymentSplitFromPaid(rawPaid: number | null, total?: number): void;
  abstract syncPaymentSplitFromDue(rawDue: number | null, total?: number): void;
  abstract enforceNoCustomerCreditRestrictions(): void;
  abstract resetSearchAndPickerState(): void;
  abstract applyProductDefaultsForLine(
    clientLineKey: string,
    itemName: string,
    barcode: string,
  ): void;
  abstract getEventNotificationKey(eventType: string): string;
  abstract detectAndHighlightChangedRows(
    oldPreview: SalePreviewDto | null,
    newPreview: SalePreviewDto,
  ): void;
  abstract resetTransientState(): void;
  abstract refreshPendingCheckouts(): Promise<void>;
  abstract searchOfflineCatalog(term: string): Promise<void>;
  abstract refreshOfflinePreview(cart: readonly CartItem[]): Promise<void>;
  abstract onOfflineSubmit(): Promise<void>;
  abstract printA4(saleId: string): void;
  abstract printThermal(saleId: string): void;
  abstract printOfflineA4(): void;
  abstract printOfflineThermal(): void;

  protected buildCreditNoteRedemptions(): CreditNoteRedemptionRequest[] {
    return this.appliedCreditNotes()
      .filter((note) => note.amount > 0)
      .map((note) => ({
        creditNoteId: note.creditNoteId,
        code: note.code,
        amount: note.amount,
      }));
  }

  refreshCreditNoteCustomerMismatchState(): void {}
}
