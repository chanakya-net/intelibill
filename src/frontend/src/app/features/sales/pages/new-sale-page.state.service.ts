import { computed, DestroyRef, inject, Injectable, Injector, signal } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { CURRENCY_ADDON_PT, CURRENCY_INPUT_GROUP_PT, CURRENCY_INPUT_NUMBER_PT } from '../../../shared/primeng-pt.config';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { ShopUpdatesSignalRService } from '../../../core/services/shop-updates-signalr.service';
import { CustomersFacade } from '../../customers/state/customers.facade';
import { InventoryService } from '../../inventory/services/inventory.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { PAYMENT_METHOD_VALUES, PaymentMethod } from '../services/sale.models';
import type {
  SalePreviewDto,
  SalePreviewLineDto,
} from '../services/sale.models';
import { SaleCartStateService, CartItem } from '../services/sale-cart-state.service';
import { OfflineSaleStateService } from '../services/offline-sale-state.service';
import { SalePreviewService } from '../services/sale-preview.service';
import { SalesFacade } from '../state/sales.facade';
import { CustomerDto } from '../components/new-sale/sale-customer-section.component';
import { PaymentMethodOption } from '../components/new-sale/sale-payment-section.component';
import { CreateSaleResponse } from '../components/new-sale/sale-confirmation-dialog.component';
import { SyncStatus } from '../components/new-sale/offline-status-banner.component';

const STALE_INTERVAL_4H = 4 * 60 * 60 * 1000;
const MAX_OFFLINE_AGE_MS = 48 * 60 * 60 * 1000;

@Injectable()
export abstract class NewSalePageStateService {
  protected readonly cartRetentionMs = 5 * 60 * 1000;
  protected readonly offlineClockIntervalMs = 60 * 1000;
  protected readonly nowTick = signal(Date.now());
  protected readonly fb = inject(FormBuilder);
  protected readonly router = inject(Router);
  protected readonly authService = inject(AuthService);
  protected readonly catalogSync = inject(ProductCatalogSyncService);
  protected readonly inventoryService = inject(InventoryService);
  protected readonly customersFacade = inject(CustomersFacade);
  protected readonly salesFacade = inject(SalesFacade);
  protected readonly cartState = inject(SaleCartStateService);
  protected readonly salePreview = inject(SalePreviewService);
  protected readonly shopUpdatesService = inject(ShopUpdatesSignalRService);
  protected readonly destroyRef = inject(DestroyRef);
  protected readonly injector = inject(Injector);
  protected readonly networkStatus = inject(NetworkStatusService);
  protected readonly offlineSaleState = inject(OfflineSaleStateService);

  readonly paymentMethods = PAYMENT_METHOD_VALUES;
  readonly cart = this.cartState.cart;
  readonly searchInput = signal('');
  readonly searchSuggestions = signal<string[]>([]);
  readonly isSearchingBatches = signal(false);
  readonly batchSearchError = signal('');
  readonly availableBatches = signal<readonly AvailableBatchDto[]>([]);
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
  readonly selectedBatch = signal<AvailableBatchDto | null>(null);
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
  readonly lastMutationSucceeded = this.salesFacade.lastMutationSucceeded;
  readonly lastMutationType = this.salesFacade.lastMutationType;
  readonly lastRecordedSale = this.salesFacade.lastRecordedSale;
  readonly customers = this.customersFacade.allCustomers;
  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly cartBootstrapped = this.cartState.cartBootstrapped;

  // Offline mode signals
  readonly deviceSettings = this.offlineSaleState.offlineDeviceSettings;
  readonly snapshotCompletedAt = this.offlineSaleState.snapshotCompletedAt;
  readonly offlinePendingCount = this.offlineSaleState.offlinePendingCount;
  readonly offlineNeedsReviewCount = this.offlineSaleState.offlineNeedsReviewCount;
  readonly offlineInvoiceRemaining = this.offlineSaleState.offlineInvoiceRemaining;
  readonly offlineCatalog = this.offlineSaleState.offlineCatalog;
  readonly offlineCustomers = this.offlineSaleState.offlineCustomers;
  readonly isOfflineSubmitting = signal(false);
  readonly offlineConfirmation = signal<{ invoiceNumber: string; grandTotal: number; clientSaleId: string } | null>(null);
  readonly showOfflineConfirmation = signal(false);

  readonly subtotalAmount = computed(() => {
    const preview = this.checkoutPreview();
    if (preview !== null) {
      return preview.totalAmount - preview.totalTaxAmount;
    }
    return this.cart().reduce((sum, item) => sum + this.getLineSubtotal(item), 0);
  });
  readonly totalTaxAmount = computed(() => {
    const preview = this.checkoutPreview();
    if (preview !== null) {
      return preview.totalTaxAmount;
    }
    return this.cart().reduce((sum, item) => sum + this.getLineTaxAmount(item), 0);
  });
  readonly totalAmount = computed(() => {
    const preview = this.checkoutPreview();
    if (preview !== null) {
      return preview.totalAmount;
    }
    return this.cart().reduce((sum, item) => sum + this.getLineTotal(item), 0);
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
  readonly canSelectCreditPaymentMethod = computed(() => this.isOfflineMode() || this.canUseCredit());
  readonly paymentMethodsForSelection = computed(() =>
    this.paymentMethods.filter((method) => method.value !== 4 || this.canSelectCreditPaymentMethod())
  );
  readonly selectedPaymentMethod = computed(() =>
    this.getPaymentMethodLabel(this.paymentForm.controls.paymentMethod.value)
  );

  readonly totalDiscountAmount = computed(() => this.checkoutPreview()?.totalDiscountAmount ?? 0);
  readonly batchPickerQuantity = signal(1);

  readonly isLineDiscountEditorOpenFn = (itemId: string): boolean => this.isLineDiscountEditorOpen(itemId);
  readonly hasTaxFn = (item: CartItem): boolean => this.hasTax(item);
  readonly canIncreaseFn = (item: CartItem): boolean => this.canIncreaseCartItem(item);
  readonly getLineSubtotalFn = (item: CartItem): number => this.getLineSubtotal(item);
  readonly getLineTaxAmountFn = (item: CartItem): number => this.getLineTaxAmount(item);
  readonly getLineTotalFn = (item: CartItem): number => this.getLineTotal(item);
  readonly getUnitSubtotalFn = (item: CartItem): number => this.getUnitSubtotal(item);
  readonly getUnitTaxAmountFn = (item: CartItem): number => this.getUnitTaxAmount(item);
  readonly getUnitFinalPriceFn = (item: CartItem): number => this.getUnitFinalPrice(item);
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
    batchNumber: ['', Validators.required],
    quantity: [1, [Validators.required, Validators.min(1)]],
  });

  readonly customerForm = this.fb.nonNullable.group({
    customerName: ['', Validators.maxLength(180)],
    customerPhone: ['', [Validators.maxLength(32), Validators.pattern(/^[+]?\d{7,15}$/)]],
  });

  readonly paymentForm = this.fb.nonNullable.group({
    paymentMethod: [1, Validators.required],
    paidAmount: [0, [Validators.required, Validators.min(0)]],
    dueAmount: [0, [Validators.required, Validators.min(0)]],
  });

  protected initialized = false;

  abstract onAddToCart(): void;
  abstract onIncreaseCartItem(index: number): void;
  abstract onDecreaseCartItem(index: number): void;
  abstract onRemoveCartItem(index: number): void;
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
  abstract onCartItemDiscountValueChange(clientLineKey: string, value: number | null | undefined): void;
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
  abstract applyProductDefaultsForLine(clientLineKey: string, itemName: string, barcode: string): void;
  abstract getEventNotificationKey(eventType: string): string;
  abstract detectAndHighlightChangedRows(oldPreview: SalePreviewDto | null, newPreview: SalePreviewDto): void;
  abstract resetTransientState(): void;
  abstract searchOfflineCatalog(term: string): Promise<void>;
  abstract refreshOfflinePreview(cart: readonly CartItem[]): Promise<void>;
  abstract onOfflineSubmit(): Promise<void>;
  abstract printA4(saleId: string): void;
  abstract printThermal(saleId: string): void;
  abstract printOfflineA4(): void;
  abstract printOfflineThermal(): void;
}
