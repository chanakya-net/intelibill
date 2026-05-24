import { CommonModule } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { DividerModule } from 'primeng/divider';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { CURRENCY_ADDON_PT, CURRENCY_INPUT_GROUP_PT, CURRENCY_INPUT_NUMBER_PT } from '../../../shared/primeng-pt.config';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { ShopUpdatesSignalRService } from '../../../core/services/shop-updates-signalr.service';
import { OfflineSalesDeviceSettings, OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineCustomerLiteSnapshot, OfflineSalesSnapshotIndexedDbService, OfflineSellableBatchSnapshot } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import { Customer } from '../../customers/services/customer.service';
import { CustomersFacade } from '../../customers/state/customers.facade';
import { AvailableBatchDto, InventoryService } from '../../inventory/services/inventory.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import {
  InstantDiscountRequest,
  NO_DISCOUNT,
  RecordSaleItemRequest,
  RecordSaleRequest,
  SalePreviewDto,
  SalePreviewLineDto,
  PAYMENT_METHOD_VALUES,
} from '../services/sale.service';
import { OfflineFinalizeRequest, OfflineSaleFinalizationService } from '../services/offline-sale-finalization.service';
import { OfflineSalePricingInput, OfflineSalePricingLineInput } from '../services/offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from '../services/offline-sales-queue-indexeddb.service';
import { calculateOfflineFrozenSale } from '../services/offline-sale-pricing-calculator';
import { SaleCartStateService, CartItem } from '../services/sale-cart-state.service';
import { SalePreviewService } from '../services/sale-preview.service';
import { SalesFacade } from '../state/sales.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import { DateOnlyPipe } from '../../../shared/pipes/date-only.pipe';

const STALE_INTERVAL_4H = 4 * 60 * 60 * 1000;
const MAX_OFFLINE_AGE_MS = 48 * 60 * 60 * 1000;

@Component({
  selector: 'app-new-sale-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    BarcodeScannerDialogComponent,
    AutoCompleteModule,
    ButtonModule,
    CardModule,
    DialogModule,
    DividerModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputTextModule,
    InputNumberModule,
    ProgressSpinnerModule,
    SelectModule,
    TableModule,
    TagModule,
    TranslocoPipe,
    DateOnlyPipe,
  ],
  templateUrl: './new-sale-page.component.html',
  styleUrl: './new-sale-page.component.scss',
})
export class NewSalePageComponent {
  private readonly cartRetentionMs = 5 * 60 * 1000;
  private readonly offlineClockIntervalMs = 60 * 1000;
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly inventoryService = inject(InventoryService);
  private readonly customersFacade = inject(CustomersFacade);
  private readonly salesFacade = inject(SalesFacade);
  private readonly cartState = inject(SaleCartStateService);
  private readonly salePreview = inject(SalePreviewService);
  private readonly shopUpdatesService = inject(ShopUpdatesSignalRService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly networkStatus = inject(NetworkStatusService);
  private readonly offlineFinalization = inject(OfflineSaleFinalizationService);
  private readonly deviceSettingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly offlineSnapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly offlineQueueDb = inject(OfflineSalesQueueIndexedDbService);

  readonly paymentMethods = PAYMENT_METHOD_VALUES;
  readonly cart = this.cartState.cart;
  readonly searchInput = signal('');
  readonly searchSuggestions = signal<string[]>([]);
  readonly isSearchingBatches = signal(false);
  readonly batchSearchError = signal('');
  readonly availableBatches = signal<AvailableBatchDto[]>([]);
  readonly showBatchPicker = signal(false);
  readonly selectedBatch = signal<AvailableBatchDto | null>(null);
  readonly selectedCustomerId = signal<string | null>(null);
  readonly selectedCustomerName = signal<string | null>(null);
  readonly customerNameSuggestions = signal<string[]>([]);
  readonly isScannerOpen = signal(false);
  readonly isWalkIn = signal(true);
  readonly paymentSplitError = signal('');
  readonly lastEditedPaymentField = signal<'paid' | 'due'>('due');
  private isSyncingPaymentControls = false;

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
  readonly deviceSettings = signal<OfflineSalesDeviceSettings | null>(null);
  readonly snapshotCompletedAt = signal<string | null>(null);
  readonly offlinePendingCount = signal<number>(0);
  readonly offlineNeedsReviewCount = signal<number>(0);
  readonly offlineInvoiceRemaining = signal<number>(0);
  readonly isOfflineSubmitting = signal(false);
  readonly offlineSubmitError = signal('');
  readonly offlineConfirmation = signal<{ invoiceNumber: string; grandTotal: number; clientSaleId: string } | null>(null);
  readonly showOfflineConfirmation = signal(false);
  private offlineCatalogCache = signal<readonly OfflineSellableBatchSnapshot[] | null>(null);
  private offlineCatalogCacheKey = signal<string | null>(null);
  private readonly offlineCachedCustomers = signal<readonly OfflineCustomerLiteSnapshot[]>([]);
  private readonly nowTick = signal(Date.now());

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

  readonly isOfflineMode = computed(() => !this.networkStatus.canReachApi());

  readonly isOfflineEligible = computed(() => {
    const settings = this.deviceSettings();
    return !!settings?.enabled;
  });

  readonly snapshotAgeMs = computed(() => {
    const completedAt = this.snapshotCompletedAt();
    this.nowTick();
    if (!completedAt) return null;
    const ms = Date.now() - Date.parse(completedAt);
    return Number.isFinite(ms) ? ms : null;
  });

  readonly staleWarningCount = computed(() => {
    const ageMs = this.snapshotAgeMs();
    if (ageMs === null || ageMs <= 0) return 0;
    return Math.floor(ageMs / STALE_INTERVAL_4H);
  });

  readonly isSnapshotTooOld = computed(() => {
    const ageMs = this.snapshotAgeMs();
    if (ageMs === null) return false;
    return ageMs > MAX_OFFLINE_AGE_MS;
  });

  readonly snapshotAgeHours = computed(() => {
    const ageMs = this.snapshotAgeMs();
    if (ageMs === null) return null;
    return Math.floor(ageMs / (60 * 60 * 1000));
  });

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

  constructor() {
    const offlineClockId = window.setInterval(() => {
      this.nowTick.set(Date.now());
    }, this.offlineClockIntervalMs);
    this.destroyRef.onDestroy(() => window.clearInterval(offlineClockId));

    this.customersFacade.loadCustomers();
    this.salesFacade.clearError();
    this.salesFacade.clearMutationStatus();

    this.customerForm.controls.customerName.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((value) => {
        const typedName = value.trim().toLowerCase();
        const selectedName = this.selectedCustomerName();

        if (!typedName || !selectedName || typedName !== selectedName) {
          this.selectedCustomerId.set(null);
          this.selectedCustomerName.set(null);
          if (!this.isOfflineMode()) {
            this.enforceNoCustomerCreditRestrictions();
          }
        }
      });

    this.paymentForm.controls.paidAmount.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((value) => {
        if (this.isSyncingPaymentControls) {
          return;
        }

        this.lastEditedPaymentField.set('paid');
        this.syncPaymentSplitFromPaid(value);
      });

    this.paymentForm.controls.dueAmount.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((value) => {
        if (this.isSyncingPaymentControls) {
          return;
        }

        this.lastEditedPaymentField.set('due');
        this.syncPaymentSplitFromDue(value);
      });

    const handlePreviewApplied = (preview: SalePreviewDto, oldPreview: SalePreviewDto | null): void => {
      this.detectAndHighlightChangedRows(oldPreview, preview);
      this.revalidateDiscountsAgainstPreview();
    };

    this.salePreview.refreshOnlinePreview({
      getCart: () => this.cart(),
      getSaleDiscount: () => ({ type: this.saleDiscountType(), value: this.saleDiscountValue() }),
      onPreviewApplied: handlePreviewApplied,
    });

    this.salePreview.refreshOnServerUpdate({
      getCart: () => this.cart(),
      getSaleDiscount: () => ({ type: this.saleDiscountType(), value: this.saleDiscountValue() }),
      onPreviewApplied: handlePreviewApplied,
    });

    // Subscribe to server updates and trigger immediate preview refresh (no debounce)
    this.shopUpdatesService.updates$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((update) => {
        const cart = this.cart();
        if (cart.length === 0) {
          return;
        }

        // Show notification for incoming update
        this.updateNotificationText.set(`sales.newSale.shopUpdate.${this.getEventNotificationKey(update.eventType)}`);
        this.showUpdateNotification.set(true);
        setTimeout(() => this.showUpdateNotification.set(false), 2000);

        // Immediately trigger preview refresh for server updates
        this.salePreview.triggerServerUpdateRefresh();
      });

    // Connect to shop updates on initialization
    void this.shopUpdatesService.startConnection();

    effect(() => {
      if (this.lastMutationSucceeded()) {
        const type = this.lastMutationType();
        if (type === 'record-sale') {
          this.showConfirmation.set(true);
          this.resetTransientState();
          return;
        } else {
          this.salesFacade.clearMutationStatus();
          this.router.navigate(['/sales']);
        }
      }
    });

    effect(() => {
      const total = this.totalAmount();
      if (this.lastEditedPaymentField() === 'paid') {
        this.syncPaymentSplitFromPaid(this.paymentForm.controls.paidAmount.value, total);
      } else {
        this.syncPaymentSplitFromDue(this.paymentForm.controls.dueAmount.value, total);
      }
    });

    effect(() => {
      const dueControl = this.paymentForm.controls.dueAmount;

      if (this.isOfflineMode() || this.canUseCredit()) {
        if (dueControl.disabled) {
          dueControl.enable({ emitEvent: false });
        }
        return;
      }

      this.enforceNoCustomerCreditRestrictions();
    });

    effect(() => {
      this.activeShopId();
      void this.cartState.loadPersistedCart(this.cartRetentionMs, (row) => {
        this.applyProductDefaultsForLine(row.clientLineKey, row.itemName, row.barcode);
      });
    });

    effect(() => {
      this.activeShopId();
      this.cart();
      if (!this.cartBootstrapped()) {
        return;
      }

      void this.cartState.persistCart();
    });

    // Load device settings and snapshot info reactively
    effect(() => {
      const shopId = this.activeShopId();
      if (!shopId) {
        this.deviceSettings.set(null);
        this.snapshotCompletedAt.set(null);
        this.offlineCatalogCache.set(null);
        this.offlineCatalogCacheKey.set(null);
        this.offlineCachedCustomers.set([]);
        return;
      }
      const settings = this.deviceSettingsStorage.loadSettings(shopId);
      this.deviceSettings.set(settings);
      this.offlineCatalogCache.set(null);
      this.offlineCatalogCacheKey.set(null);
      if (settings?.enabled) {
        void this.refreshOfflineState(shopId, settings.deviceId);
      }
    });

    // Schedule preview whenever cart changes (after bootstrap). Item-level discount
    // edits always go through cart mutations, so they reach this effect automatically.
    effect(() => {
      const cart = this.cart();
      this.isOfflineMode();
      this.isOfflineEligible();
      this.snapshotCompletedAt();
      this.saleDiscountType();
      this.saleDiscountValue();
      if (!this.cartBootstrapped()) {
        return;
      }
      const hasOverrideErrors = this.revalidateLineOverrides(cart);
      if (cart.length === 0) {
        this.checkoutPreview.set(null);
        this.isPreviewLoading.set(false);
        this.previewError.set('');
        return;
      }
      if (hasOverrideErrors) {
        this.checkoutPreview.set(null);
        this.isPreviewLoading.set(false);
        this.previewError.set('sales.newSale.overrides.invalid');
        return;
      }
      this.previewError.set('');
      if (this.isOfflineMode()) {
        if (this.isOfflineEligible()) {
          void this.refreshOfflinePreview(cart);
        } else {
          this.salePreview.clearPreviewState();
        }
        return;
      }
      this.salePreview.triggerPreview();
    });
  }

  async onBarcodeSearch(): Promise<void> {
    const searchTerm = this.searchInput().trim();
    if (!searchTerm) return;

    if (this.isOfflineMode() && this.isOfflineEligible()) {
      await this.searchOfflineCatalog(searchTerm);
      return;
    }

    this.batchSearchError.set('');
    this.isSearchingBatches.set(true);
    this.availableBatches.set([]);

    this.inventoryService.getAvailableBatchesBySearchTerm(searchTerm).subscribe({
      next: (batches) => {
        this.isSearchingBatches.set(false);
        const list = [...batches];
        if (list.length === 0) {
          this.batchSearchError.set('sales.newSale.noBatchesFound');
          return;
        }
        if (list.length === 1) {
          const result = this.cartState.addBatchToCart(list[0], 1);
          if (!result.added) {
            this.batchSearchError.set('sales.newSale.exceedsStock');
            return;
          }
          if (result.addedLineKey && !this.isOfflineMode()) {
            this.applyProductDefaultsForLine(result.addedLineKey, list[0].itemName, list[0].barcode);
          }
          this.resetSearchAndPickerState();
        } else {
          this.availableBatches.set(list);
          this.showBatchPicker.set(true);
        }
      },
      error: (err) => {
        this.isSearchingBatches.set(false);
        this.batchSearchError.set(err.error?.detail || 'sales.newSale.searchError');
      },
    });
  }

  onFilterSearch(event: AutoCompleteCompleteEvent): void {
    const query = event.query.trim();
    if (!query) {
      this.searchSuggestions.set([]);
      return;
    }

    const names = this.catalogSync.filterByName(query).map((e) => e.name);
    const barcodes = this.catalogSync.filterByBarcode(query).map((e) => e.barcode);
    const merged = [...new Set([...names, ...barcodes])];
    this.searchSuggestions.set(merged.slice(0, 20));
  }

  onSearchSuggestionSelected(value: string): void {
    this.searchInput.set(value?.trim() ?? '');
  }

  onFilterCustomerName(event: AutoCompleteCompleteEvent): void {
    const query = event.query.trim().toLowerCase();

    if (this.isOfflineMode()) {
      const filtered = this.offlineCachedCustomers()
        .filter((c) => !query || c.name.toLowerCase().includes(query))
        .map((c) => c.name.trim())
        .filter((name) => name.length > 0);
      this.customerNameSuggestions.set([...new Set(filtered)].slice(0, 20));
      return;
    }

    const activeCustomers = this.customers().filter((customer) => customer.isActive);
    const filteredNames = activeCustomers
      .filter((customer) => !query || customer.name.toLowerCase().includes(query))
      .map((customer) => customer.name.trim())
      .filter((name) => name.length > 0);

    const uniqueNames = [...new Set(filteredNames)];
    this.customerNameSuggestions.set(uniqueNames.slice(0, 20));
  }

  onCustomerSuggestionSelected(name: string): void {
    const normalizedName = name.trim().toLowerCase();
    if (!normalizedName) {
      this.selectedCustomerId.set(null);
      this.selectedCustomerName.set(null);
      return;
    }

    if (this.isOfflineMode()) {
      const matches = this.offlineCachedCustomers().filter(
        (c) => c.name.trim().toLowerCase() === normalizedName
      );
      if (matches.length !== 1) {
        this.selectedCustomerId.set(null);
        this.selectedCustomerName.set(null);
        return;
      }
      const [offlineCustomer] = matches;
      this.selectedCustomerId.set(offlineCustomer.customerId);
      this.selectedCustomerName.set(normalizedName);
      this.customerForm.patchValue(
        { customerName: offlineCustomer.name, customerPhone: offlineCustomer.phoneNumber },
        { emitEvent: false }
      );
      return;
    }

    const matches = this.customers().filter(
      (customer) => customer.isActive && customer.name.trim().toLowerCase() === normalizedName
    );

    if (matches.length !== 1) {
      this.selectedCustomerId.set(null);
      this.selectedCustomerName.set(null);
      return;
    }

    const [customer] = matches;
    this.selectedCustomerId.set(customer.customerId);
    this.selectedCustomerName.set(normalizedName);
    this.customerForm.patchValue(
      {
        customerName: customer.name,
        customerPhone: customer.phoneNumber,
      },
      { emitEvent: false }
    );
  }

  openScanner(): void {
    this.isScannerOpen.set(true);
  }

  onScannerVisibilityChange(visible: boolean): void {
    this.isScannerOpen.set(visible);
  }

  onScannedBarcode(detection: BarcodeDetection): void {
    const barcode = detection.value.trim();
    if (!barcode) {
      return;
    }

    this.searchInput.set(barcode);
    this.isScannerOpen.set(false);
    void this.onBarcodeSearch();
  }

  onSelectBatch(batch: AvailableBatchDto): void {
    this.selectedBatch.set(batch);
    this.batchPickerForm.patchValue({ batchNumber: batch.batchNumber, quantity: 1 });
  }

  onAddToCart(): void {
    if (this.batchPickerForm.invalid) {
      this.batchPickerForm.markAllAsTouched();
      return;
    }
    const batch = this.selectedBatch();
    if (!batch) return;

    const qty = this.batchPickerForm.controls.quantity.value;
    if (qty > batch.quantity) {
      this.batchPickerForm.controls.quantity.setErrors({ exceedsStock: true });
      return;
    }

    const result = this.cartState.addBatchToCart(batch, qty);
    if (!result.added) {
      this.batchPickerForm.controls.quantity.setErrors({ exceedsStock: true });
      this.batchSearchError.set('sales.newSale.exceedsStock');
      return;
    }

    if (result.addedLineKey && !this.isOfflineMode()) {
      this.applyProductDefaultsForLine(result.addedLineKey, batch.itemName, batch.barcode);
    }

    this.resetSearchAndPickerState();
  }

  onIncreaseCartItem(index: number): void {
    this.cartState.onIncreaseCartItem(index);
  }

  onDecreaseCartItem(index: number): void {
    this.cartState.onDecreaseCartItem(index);
  }

  canIncreaseCartItem(item: CartItem): boolean {
    return this.cartState.canIncreaseCartItem(item);
  }

  onRemoveCartItem(index: number): void {
    this.cartState.onRemoveCartItem(index);
  }

  onClearCart(): void {
    this.cartState.onClearCart();
  }

  hasTax(item: CartItem): boolean {
    return this.cartState.hasTax(item);
  }

  getLineSubtotal(item: CartItem): number {
    return this.cartState.getLineSubtotal(item);
  }

  getLineTaxAmount(item: CartItem): number {
    return this.cartState.getLineTaxAmount(item);
  }

  getLineTotal(item: CartItem): number {
    return this.cartState.getLineTotal(item);
  }

  getUnitSubtotal(item: CartItem): number {
    return this.cartState.getUnitSubtotal(item);
  }

  getUnitTaxAmount(item: CartItem): number {
    return this.cartState.getUnitTaxAmount(item);
  }

  getUnitFinalPrice(item: CartItem): number {
    return this.cartState.getUnitFinalPrice(item);
  }

  async onSubmit(): Promise<void> {
    if (this.isSubmitting()) return;
    if (this.isOfflineSubmitting()) return;
    if (this.cart().length === 0) return;
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    this.paymentSplitError.set('');

    // OFFLINE BRANCH
    if (this.isOfflineMode()) {
      if (this.isOfflineEligible()) {
        const blockingErrorKey = this.getBlockingCartValidationErrorKey(this.cart());
        if (blockingErrorKey) {
          this.paymentSplitError.set(blockingErrorKey);
          return;
        }
        await this.onOfflineSubmit();
      } else {
        this.paymentSplitError.set('sales.newSale.offline.blockDeviceNotEnabled');
      }
      return;
    }

    if (this.checkoutPreview() === null) {
      this.paymentSplitError.set(this.previewError() || 'sales.newSale.previewRequired');
      return;
    }

    const blockingErrorKey = this.getBlockingCartValidationErrorKey(this.cart());
    if (blockingErrorKey) {
      this.paymentSplitError.set(blockingErrorKey);
      return;
    }

    const items: RecordSaleItemRequest[] = this.cart().map((item) => ({
      barcode: item.barcode,
      batchNumber: item.batchNumber,
      itemName: item.itemName,
      quantity: item.quantity,
      costPrice: item.costPrice,
      salesPrice: item.salesPrice,
      mrp: item.mrp,
      taxRatePercent: item.taxRatePercent,
      isPriceIncludingTax: item.taxIncluded,
      inventoryBatchId: item.inventoryBatchId,
      clientLineKey: item.clientLineKey,
      itemDiscount: { type: item.itemDiscountType as 0 | 1 | 2, value: item.itemDiscountValue } satisfies InstantDiscountRequest,
      hsnCode: item.hsnCode ?? null,
    }));

    const customerName = this.customerForm.controls.customerName.value.trim() || null;
    const customerPhone = this.customerForm.controls.customerPhone.value.trim() || null;
    const paidAmount = this.toFiniteAmount(this.paymentForm.controls.paidAmount.value);
    const dueAmount = this.toFiniteAmount(this.paymentForm.controls.dueAmount.value);
    const totalAmount = this.roundAmount(this.totalAmount());

    if (
      !Number.isFinite(paidAmount) ||
      !Number.isFinite(dueAmount) ||
      paidAmount < 0 ||
      dueAmount < 0 ||
      !this.areAmountsEqual(paidAmount + dueAmount, totalAmount)
    ) {
      this.paymentSplitError.set('sales.newSale.invalidPaymentSplit');
      return;
    }

    if (dueAmount > 0 && !this.selectedCustomerId()) {
      this.paymentSplitError.set('sales.newSale.customerRequiredForDue');
      return;
    }

    if (this.paymentForm.controls.paymentMethod.value === 4 && !this.canUseCredit()) {
      this.paymentSplitError.set('sales.newSale.creditRequiresCustomerPhone');
      return;
    }

    const request: RecordSaleRequest = {
      idempotencyKey: this.createSaleIdempotencyKey(),
      customerId: this.selectedCustomerId(),
      customerName,
      customerPhone,
      paymentMethod: this.paymentForm.controls.paymentMethod.value,
      paidAmount,
      dueAmount,
      items,
      saleDiscount: { type: this.saleDiscountType(), value: this.saleDiscountValue() },
    };

    this.salesFacade.recordSale(request);
  }

  private createSaleIdempotencyKey(): string {
    if (globalThis.crypto?.randomUUID) {
      return `sale-${globalThis.crypto.randomUUID()}`;
    }

    return `sale-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
  }

  onCancel(): void {
    this.router.navigate(['/sales']);
  }

  toggleSaleDiscountEditor(): void {
    this.isSaleDiscountEditorOpen.update((v) => !v);
  }

  onSaleDiscountTypeChange(type: 0 | 1 | 2): void {
    this.saleDiscountError.set('');
    const preview = this.checkoutPreview();
    if (preview) {
      const limits = this.getSaleDiscountLimits();
      if (!limits.isEligible || (type === 1 && limits.maxPercent <= 0) || (type === 2 && limits.maxFlat <= 0)) {
        this.saleDiscountType.set(0);
        this.saleDiscountValue.set(0);
        return;
      }
    }

    this.saleDiscountType.set(type);
    if (type === 0) {
      this.saleDiscountValue.set(0);
      return;
    }

    // Re-validate current value under new type.
    this.onSaleDiscountValueChange(this.saleDiscountValue());
  }

  onSaleDiscountValueChange(value: number | null | undefined): void {
    const normalized = this.roundAmount(Math.max(0, Number(value ?? 0)));
    const type = this.saleDiscountType();

    if (type === 0) {
      this.saleDiscountValue.set(0);
      this.saleDiscountError.set('');
      return;
    }

    const preview = this.checkoutPreview();
    if (!preview) {
      if (type === 1 && normalized > 100) {
        this.saleDiscountError.set('sales.newSale.discounts.exceedsMaxPercent');
        return;
      }
      this.saleDiscountError.set('');
      this.saleDiscountValue.set(normalized);
      return;
    }

    const limits = this.getSaleDiscountLimits();
    if (!limits.isEligible) {
      this.saleDiscountError.set('sales.newSale.discounts.saleNotEligible');
      this.saleDiscountValue.set(0);
      return;
    }

    const maxAllowed = type === 1 ? limits.maxPercent : limits.maxFlat;
    if (normalized > maxAllowed) {
      this.saleDiscountError.set(
        type === 1 ? 'sales.newSale.discounts.exceedsMaxPercent' : 'sales.newSale.discounts.exceedsMaxFlat'
      );
      return; // block invalid input
    }

    this.saleDiscountError.set('');
    this.saleDiscountValue.set(normalized);
  }

  isSaleDiscountEligible(): boolean {
    return this.getSaleDiscountLimits().isEligible;
  }

  getPreviewLine(clientLineKey: string): SalePreviewLineDto | null {
    const preview = this.checkoutPreview();
    if (!preview || !clientLineKey) return null;
    return preview.lines.find((l) => l.clientLineKey === clientLineKey) ?? null;
  }

  toggleLineDiscountEditor(clientLineKey: string): void {
    if (!clientLineKey) return;
    this.openLineDiscountEditorByKey.update((current) => ({
      ...current,
      [clientLineKey]: !current[clientLineKey],
    }));
  }

  isLineDiscountEditorOpen(clientLineKey: string): boolean {
    return Boolean(this.openLineDiscountEditorByKey()[clientLineKey]);
  }

  getCartItemDiscountError(clientLineKey: string): string {
    return this.cartItemDiscountErrorByKey()[clientLineKey] ?? '';
  }

  getCartItemHsnError(clientLineKey: string): string {
    return this.cartItemHsnErrorByKey()[clientLineKey] ?? '';
  }

  getCartItemTaxError(clientLineKey: string): string {
    return this.cartItemTaxErrorByKey()[clientLineKey] ?? '';
  }

  onCartItemDiscountTypeChange(clientLineKey: string, type: 0 | 1 | 2): void {
    if (!clientLineKey) return;

    this.cartItemDiscountErrorByKey.update((current) => ({ ...current, [clientLineKey]: '' }));

    const limits = this.getLineDiscountLimits(clientLineKey);
    const isAllowed =
      type === 0 ||
      (type === 1 && limits.maxPercent > 0) ||
      (type === 2 && limits.maxFlat > 0) ||
      !Number.isFinite(type);

    const nextType = isAllowed ? type : 0;
    this.cart.update((items) =>
      items.map((item) =>
        item.clientLineKey === clientLineKey
          ? {
              ...item,
              itemDiscountType: nextType,
              itemDiscountValue: nextType === 0 ? 0 : item.itemDiscountValue,
            }
          : item
      )
    );

    if (nextType === 0) {
      return;
    }

    const current = this.cart().find((x) => x.clientLineKey === clientLineKey);
    this.onCartItemDiscountValueChange(clientLineKey, current?.itemDiscountValue ?? 0);
  }

  onCartItemDiscountValueChange(clientLineKey: string, value: number | null | undefined): void {
    if (!clientLineKey) return;
    const normalized = this.roundAmount(Math.max(0, Number(value ?? 0)));

    const item = this.cart().find((x) => x.clientLineKey === clientLineKey);
    if (!item) return;

    if (item.itemDiscountType === 0) {
      this.cartItemDiscountErrorByKey.update((current) => ({ ...current, [clientLineKey]: '' }));
      this.cart.update((items) =>
        items.map((row) => (row.clientLineKey === clientLineKey ? { ...row, itemDiscountValue: 0 } : row))
      );
      return;
    }

    const limits = this.getLineDiscountLimits(clientLineKey);
    const maxAllowed = item.itemDiscountType === 1 ? limits.maxPercent : limits.maxFlat;
    if (Number.isFinite(maxAllowed) && normalized > maxAllowed) {
      this.cartItemDiscountErrorByKey.update((current) => ({
        ...current,
        [clientLineKey]:
          item.itemDiscountType === 1
            ? 'sales.newSale.discounts.exceedsMaxPercent'
            : 'sales.newSale.discounts.exceedsMaxFlat',
      }));
      return; // block invalid input
    }

    this.cartItemDiscountErrorByKey.update((current) => ({ ...current, [clientLineKey]: '' }));
    this.cart.update((items) =>
      items.map((row) =>
        row.clientLineKey === clientLineKey ? { ...row, itemDiscountValue: normalized } : row
      )
    );
  }

  onCartItemHsnCodeChange(clientLineKey: string, value: string | null | undefined): void {
    if (!clientLineKey) return;
    const normalized = this.normalizeHsnCode(value);
    this.cart.update((items) =>
      items.map((row) =>
        row.clientLineKey === clientLineKey ? { ...row, hsnCode: normalized } : row
      )
    );
  }

  onCartItemTaxRateChange(clientLineKey: string, value: number | null | undefined): void {
    if (!clientLineKey) return;
    const raw = Number(value ?? 0);
    const normalized = Number.isFinite(raw) ? this.roundAmount(raw) : 0;
    this.cart.update((items) =>
      items.map((row) =>
        row.clientLineKey === clientLineKey ? { ...row, taxRatePercent: normalized } : row
      )
    );
  }

  private getLineDiscountLimits(clientLineKey: string): { maxFlat: number; maxPercent: number } {
    const preview = this.checkoutPreview();
    const line = preview?.lines.find((l) => l.clientLineKey === clientLineKey) ?? null;
    if (!line) {
      return { maxFlat: Number.POSITIVE_INFINITY, maxPercent: 100 };
    }

    const baseMaxFlat = Math.max(0, Number(line.maxAllowedItemDiscountFlat ?? 0));
    const baseMaxPercent = Math.max(0, Number(line.maxAllowedItemDiscountPercent ?? 0));

    if (line.configuredBatchRulePercentage == null) {
      return { maxFlat: baseMaxFlat, maxPercent: baseMaxPercent };
    }

    const configuredPercent = Math.max(0, Number(line.configuredBatchRulePercentage));
    const configuredAmount = this.roundAmount((Number(line.preTaxAmountBeforeDiscount) * configuredPercent) / 100);

    return {
      maxFlat: Math.min(baseMaxFlat, configuredAmount),
      maxPercent: Math.min(baseMaxPercent, configuredPercent),
    };
  }

  private revalidateDiscountsAgainstPreview(): void {
    this.revalidateSaleDiscountAgainstPreview();
    this.revalidateLineDiscountsAgainstPreview();
  }

  private revalidateSaleDiscountAgainstPreview(): void {
    const type = this.saleDiscountType();
    if (type === 0) {
      this.saleDiscountValue.set(0);
      this.saleDiscountError.set('');
      return;
    }

    const limits = this.getSaleDiscountLimits();
    const maxAllowed = type === 1 ? limits.maxPercent : limits.maxFlat;
    const isAllowed = limits.isEligible && maxAllowed > 0;
    if (!isAllowed) {
      this.saleDiscountType.set(0);
      this.saleDiscountValue.set(0);
      this.saleDiscountError.set('');
      return;
    }

    const normalized = this.roundAmount(Math.max(0, Number(this.saleDiscountValue() ?? 0)));
    if (normalized > maxAllowed) {
      // Clamp down to the new max so stale higher values cannot be submitted.
      this.saleDiscountValue.set(this.roundAmount(maxAllowed));
    }
    this.saleDiscountError.set('');
  }

  private revalidateLineDiscountsAgainstPreview(): void {
    const cart = this.cart();
    if (cart.length === 0) {
      this.cartItemDiscountErrorByKey.set({});
      return;
    }

    const updatesByKey: Record<
      string,
      { nextType: 0 | 1 | 2; nextValue: number; nextError: string } | undefined
    > = {};

    for (const item of cart) {
      const key = item.clientLineKey;
      if (!key) continue;

      const currentType = (item.itemDiscountType ?? 0) as 0 | 1 | 2;
      if (currentType === 0) {
        updatesByKey[key] = { nextType: 0, nextValue: 0, nextError: '' };
        continue;
      }

      const limits = this.getLineDiscountLimits(key);
      const maxAllowed = currentType === 1 ? limits.maxPercent : limits.maxFlat;
      const isAllowed = Number.isFinite(maxAllowed) ? maxAllowed > 0 : true;

      if (!isAllowed) {
        updatesByKey[key] = { nextType: 0, nextValue: 0, nextError: '' };
        continue;
      }

      const normalized = this.roundAmount(Math.max(0, Number(item.itemDiscountValue ?? 0)));
      if (Number.isFinite(maxAllowed) && normalized > maxAllowed) {
        updatesByKey[key] = {
          nextType: currentType,
          nextValue: this.roundAmount(maxAllowed),
          nextError: '',
        };
        continue;
      }

      updatesByKey[key] = { nextType: currentType, nextValue: normalized, nextError: '' };
    }

    let needsCartUpdate = false;
    let needsErrorUpdate = false;

    for (const item of cart) {
      const key = item.clientLineKey;
      const update = updatesByKey[key];
      if (!update) continue;
      if (item.itemDiscountType !== update.nextType || item.itemDiscountValue !== update.nextValue) {
        needsCartUpdate = true;
      }
      if ((this.cartItemDiscountErrorByKey()[key] ?? '') !== update.nextError) {
        needsErrorUpdate = true;
      }
    }

    if (needsErrorUpdate) {
      this.cartItemDiscountErrorByKey.update((current) => {
        const next = { ...current };
        for (const [key, update] of Object.entries(updatesByKey)) {
          if (!update) continue;
          next[key] = update.nextError;
        }
        return next;
      });
    }

    if (needsCartUpdate) {
      this.cart.update((items) =>
        items.map((item) => {
          const key = item.clientLineKey;
          const update = updatesByKey[key];
          if (!update) return item;
          if (item.itemDiscountType === update.nextType && item.itemDiscountValue === update.nextValue) {
            return item;
          }
          return { ...item, itemDiscountType: update.nextType, itemDiscountValue: update.nextValue };
        })
      );
    }
  }

  private revalidateLineOverrides(cart: readonly CartItem[]): boolean {
    if (cart.length === 0) {
      this.cartItemHsnErrorByKey.set({});
      this.cartItemTaxErrorByKey.set({});
      return false;
    }

    const hsnErrors: Record<string, string> = {};
    const taxErrors: Record<string, string> = {};

    for (const item of cart) {
      const key = item.clientLineKey;
      if (!key) continue;

      const normalizedHsn = this.normalizeHsnCode(item.hsnCode);
      if (!this.isValidHsnCode(normalizedHsn)) {
        hsnErrors[key] = 'sales.newSale.hsnInvalid';
      }

      if (!this.isValidTaxRatePercent(item.taxRatePercent)) {
        taxErrors[key] = 'sales.newSale.taxRateInvalid';
      }
    }

    this.cartItemHsnErrorByKey.set(hsnErrors);
    this.cartItemTaxErrorByKey.set(taxErrors);
    return Object.keys(hsnErrors).length > 0 || Object.keys(taxErrors).length > 0;
  }

  private getBlockingCartValidationErrorKey(cart: readonly CartItem[]): string | null {
    const hasOverrideErrors = this.revalidateLineOverrides(cart);
    if (hasOverrideErrors) {
      return 'sales.newSale.overrides.fixErrors';
    }

    if (this.saleDiscountError() || Object.values(this.cartItemDiscountErrorByKey()).some((v) => !!v)) {
      return 'sales.newSale.discounts.fixErrors';
    }

    return null;
  }

  private isValidHsnCode(value: string | null): boolean {
    if (!value) return true;
    return /^\d{4,8}$/.test(value);
  }

  private isValidTaxRatePercent(value: number): boolean {
    if (!Number.isFinite(value)) {
      return false;
    }
    if (value < 0 || value > 100) {
      return false;
    }
    return Math.abs(value - this.roundAmount(value)) < 0.0001;
  }

  private normalizeHsnCode(value: string | null | undefined): string | null {
    const trimmed = (value ?? '').trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  private getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number } {
    const preview = this.checkoutPreview();
    if (!preview) {
      return { isEligible: false, maxFlat: 0, maxPercent: 0 };
    }

    const eligibleSubtotal = Math.max(0, Number(preview.saleLevelEligibleSubtotal ?? 0));
    if (eligibleSubtotal <= 0) {
      return { isEligible: false, maxFlat: 0, maxPercent: 0 };
    }

    const totalCapacity = preview.lines.reduce((sum, line) => {
      const preTax = Number(line.preTaxAmountBeforeDiscount ?? 0);
      const itemDiscount = Number(line.itemDiscountAmount ?? 0);
      const taxableAfterItem = preTax - itemDiscount;
      const costTotal = Number(line.costPrice ?? 0) * Number(line.quantity ?? 0);
      return sum + Math.max(0, taxableAfterItem - costTotal);
    }, 0);

    let maxFlat = this.roundAmount(Math.max(0, totalCapacity));
    let maxPercent = maxFlat > 0 ? this.roundAmount((maxFlat * 100) / eligibleSubtotal) : 0;

    const configured = preview.configuredSaleRule;
    if (configured && Number.isFinite(Number(configured.percentage))) {
      const configuredPercent = Math.max(0, Number(configured.percentage));
      const configuredAmount = this.roundAmount((eligibleSubtotal * configuredPercent) / 100);
      maxFlat = Math.min(maxFlat, configuredAmount);
      maxPercent = Math.min(maxPercent, configuredPercent);
    }

    return { isEligible: maxFlat > 0, maxFlat, maxPercent };
  }

  private syncPaymentSplitFromPaid(rawPaid: number | null, total = this.totalAmount()): void {
    const paidControl = this.paymentForm.controls.paidAmount;
    const dueControl = this.paymentForm.controls.dueAmount;
    const normalizedPaid = this.normalizeAmount(rawPaid, total);
    const normalizedDue = this.roundAmount(total - normalizedPaid);

    this.isSyncingPaymentControls = true;
    try {
      if (!this.areAmountsEqual(Number(paidControl.value ?? 0), normalizedPaid)) {
        paidControl.setValue(normalizedPaid, { emitEvent: false });
      }

      if (!this.areAmountsEqual(Number(dueControl.value ?? 0), normalizedDue)) {
        dueControl.setValue(normalizedDue, { emitEvent: false });
      }
    } finally {
      this.isSyncingPaymentControls = false;
    }
  }

  private syncPaymentSplitFromDue(rawDue: number | null, total = this.totalAmount()): void {
    const paidControl = this.paymentForm.controls.paidAmount;
    const dueControl = this.paymentForm.controls.dueAmount;
    const normalizedDue = this.normalizeAmount(rawDue, total);
    const normalizedPaid = this.roundAmount(total - normalizedDue);

    this.isSyncingPaymentControls = true;
    try {
      if (!this.areAmountsEqual(Number(dueControl.value ?? 0), normalizedDue)) {
        dueControl.setValue(normalizedDue, { emitEvent: false });
      }

      if (!this.areAmountsEqual(Number(paidControl.value ?? 0), normalizedPaid)) {
        paidControl.setValue(normalizedPaid, { emitEvent: false });
      }
    } finally {
      this.isSyncingPaymentControls = false;
    }
  }

  private normalizeAmount(value: number | null | undefined, total: number): number {
    return this.roundAmount(Math.max(0, Math.min(Number(value ?? 0), total)));
  }

  private toFiniteAmount(value: number | null | undefined): number {
    const amount = Number(value ?? 0);
    return Number.isFinite(amount) ? this.roundAmount(amount) : Number.NaN;
  }

  private roundAmount(value: number): number {
    return Number(value.toFixed(2));
  }

  private areAmountsEqual(left: number, right: number): boolean {
    return this.roundAmount(left) === this.roundAmount(right);
  }

  private enforceNoCustomerCreditRestrictions(): void {
    const dueControl = this.paymentForm.controls.dueAmount;

    if (this.paymentForm.controls.paymentMethod.value === 4) {
      this.paymentForm.controls.paymentMethod.setValue(1);
    }

    if (Number(this.paymentForm.getRawValue().dueAmount ?? 0) !== 0) {
      this.lastEditedPaymentField.set('due');
      this.syncPaymentSplitFromDue(0);
    }

    if (dueControl.enabled) {
      dueControl.disable({ emitEvent: false });
    }
  }

  private resetSearchAndPickerState(): void {
    this.showBatchPicker.set(false);
    this.selectedBatch.set(null);
    this.searchInput.set('');
    this.batchPickerForm.reset({ batchNumber: '', quantity: 1 });
    this.availableBatches.set([]);
    this.batchSearchError.set('');
  }

  private applyProductDefaultsForLine(clientLineKey: string, itemName: string, barcode: string): void {
    const normalizedName = itemName?.trim();
    const normalizedBarcode = barcode?.trim();
    if (!normalizedName && !normalizedBarcode) {
      return;
    }

    this.inventoryService.getProductDetailsByNameOrBarcode(normalizedName, normalizedBarcode).subscribe({
      next: (details) => {
        const resolvedHsn = this.normalizeHsnCode(details.hsnCode);
        if (!resolvedHsn) {
          return;
        }
        this.cart.update((items) =>
          items.map((item) =>
            item.clientLineKey === clientLineKey && !this.normalizeHsnCode(item.hsnCode)
              ? { ...item, hsnCode: resolvedHsn }
              : item
          )
        );
      },
      error: () => undefined,
    });
  }

  private getEventNotificationKey(eventType: string): string {
    const keyMap: Record<string, string> = {
      'PricingChanged': 'pricingUpdated',
      'ItemApplicabilityChanged': 'itemUpdated',
      'DiscountRuleChanged': 'discountUpdated',
      'InventoryBatchVoided': 'inventoryUpdated',
    };
    return keyMap[eventType] ?? 'updated';
  }

  private detectAndHighlightChangedRows(
    oldPreview: SalePreviewDto | null,
    newPreview: SalePreviewDto
  ): void {
    const highlightedKeys = new Set<string>();

    if (!oldPreview) {
      // If no previous preview, highlight all rows
      newPreview.lines.forEach((line) => {
        if (line.clientLineKey) {
          highlightedKeys.add(line.clientLineKey);
        }
      });
    } else {
      // Compare each line with old preview and detect changes
      const oldLinesByKey = new Map(
        oldPreview.lines.map((line) => [line.clientLineKey, line])
      );

      newPreview.lines.forEach((newLine) => {
        if (!newLine.clientLineKey) return;

        const oldLine = oldLinesByKey.get(newLine.clientLineKey);
        if (!oldLine) {
          highlightedKeys.add(newLine.clientLineKey);
          return;
        }

        // Check if pricing or discount changed
        if (
          newLine.lineTotalAmount !== oldLine.lineTotalAmount ||
          newLine.itemDiscountAmount !== oldLine.itemDiscountAmount ||
          newLine.saleDiscountAmount !== oldLine.saleDiscountAmount ||
          newLine.salesPrice !== oldLine.salesPrice ||
          newLine.taxAmount !== oldLine.taxAmount
        ) {
          highlightedKeys.add(newLine.clientLineKey);
        }
      });
    }

    this.highlightedRowKeys.set(highlightedKeys);

    // Auto-clear highlights after 1.5 seconds (fade transition)
    setTimeout(() => {
      this.highlightedRowKeys.set(new Set());
    }, 1500);
  }

  private resetTransientState(): void {
    this.cartState.onClearCart();
    this.searchInput.set('');
    this.customerNameSuggestions.set([]);
    this.customerForm.reset();
    this.selectedCustomerId.set(null);
    this.selectedCustomerName.set(null);
    this.paymentForm.reset({ paymentMethod: 1, paidAmount: 0, dueAmount: 0 });
    this.paymentSplitError.set('');
    this.salePreview.clearPreviewState();
  }

  onDone(): void {
    this.showConfirmation.set(false);
    this.salesFacade.clearLastRecordedSale();
    this.salesFacade.clearMutationStatus();
    this.router.navigate(['/sales']);
  }

  onOfflineDone(): void {
    this.showOfflineConfirmation.set(false);
    this.offlineConfirmation.set(null);
    this.router.navigate(['/sales']);
  }

  private async refreshOfflineState(shopId: string, deviceId: string): Promise<void> {
    const snapshotInfo = await this.offlineSnapshotDb.getUsableSnapshotInfo(shopId);
    this.snapshotCompletedAt.set(snapshotInfo?.completedAt ?? null);

    const settings = this.resolveOfflineSettings(shopId);
    if (settings?.lastReservedLease) {
      this.offlineInvoiceRemaining.set(settings.lastReservedLease.remainingCount);
    } else {
      this.offlineInvoiceRemaining.set(0);
    }

    try {
      const customers = await this.offlineSnapshotDb.getUsableCustomers(shopId);
      this.offlineCachedCustomers.set(customers);
    } catch {
      // non-fatal
    }

    try {
      const counts = await this.offlineQueueDb.getStatusCounts(shopId, deviceId);
      this.offlinePendingCount.set(counts.Pending + counts.Syncing);
      this.offlineNeedsReviewCount.set(counts.NeedsReview);
    } catch {
      // non-fatal
    }
  }

  private async searchOfflineCatalog(term: string): Promise<void> {
    this.isSearchingBatches.set(true);
    this.batchSearchError.set('');
    this.availableBatches.set([]);
    this.showBatchPicker.set(false);

    try {
      const shopId = this.activeShopId();
      const cacheKey = `${shopId}:${this.snapshotCompletedAt() ?? ''}`;
      let catalog = this.offlineCatalogCache();
      if (!catalog || this.offlineCatalogCacheKey() !== cacheKey) {
        catalog = await this.offlineSnapshotDb.getUsableBatches(shopId);
        this.offlineCatalogCache.set(catalog);
        this.offlineCatalogCacheKey.set(cacheKey);
      }

      const q = term.toLowerCase();
      const matched = catalog.filter(
        (b) =>
          b.itemName.toLowerCase().includes(q) ||
          b.barcode.toLowerCase().startsWith(q)
      );

      if (matched.length === 0) {
        this.batchSearchError.set('sales.newSale.noBatchesFound');
        return;
      }

      const batches: AvailableBatchDto[] = matched.map((b) => ({
        barcode: b.barcode,
        itemName: b.itemName,
        batchNumber: b.batchNumber,
        inventoryBatchId: b.batchId,
        quantity: b.quantity,
        costPrice: b.costPrice,
        salesPrice: b.salesPrice,
        mrp: b.mrp,
        taxRatePercent: b.taxRatePercent,
        taxIncluded: b.taxIncluded,
        purchaseTaxIncluded: b.purchaseTaxIncluded,
        hsnCode: b.hsnCode ?? null,
        expiryDate: b.expiryDate ?? null,
      }));

      this.availableBatches.set(batches);
      this.showBatchPicker.set(true);
    } catch {
      this.batchSearchError.set('sales.newSale.searchError');
    } finally {
      this.isSearchingBatches.set(false);
    }
  }

  private async refreshOfflinePreview(cart: readonly CartItem[]): Promise<void> {
    const shopId = this.activeShopId();
    if (!shopId) {
      this.salePreview.clearPreviewState();
      return;
    }

    const requestId = this.salePreview.beginPreviewRequest();

    try {
      const rules = await this.offlineSnapshotDb.getUsableDiscountRules(shopId);
      const preview = this.buildOfflinePreview(cart, rules);
      this.salePreview.finishPreviewRequest(requestId, preview, {
        onPreviewApplied: (nextPreview, oldPreview) => {
          this.detectAndHighlightChangedRows(oldPreview, nextPreview);
          this.revalidateDiscountsAgainstPreview();
        },
      });
    } catch {
      this.salePreview.finishPreviewRequest(requestId, null, {
        failed: true,
        errorMessage: 'sales.newSale.previewError',
      });
    }
  }

  private buildOfflinePreview(
    cart: readonly CartItem[],
    rules: readonly {
      readonly ruleId: string;
      readonly ruleType: string;
      readonly inventoryBatchId?: string | null;
      readonly percentage: number;
      readonly thresholdAmount?: number | null;
    }[],
  ): SalePreviewDto {
    const pricing = calculateOfflineFrozenSale({
      soldAt: new Date().toISOString(),
      paymentMethod: this.paymentForm.controls.paymentMethod.value,
      paidAmount: this.toFiniteAmount(this.paymentForm.controls.paidAmount.value),
      customerId: this.selectedCustomerId(),
      customerName: this.customerForm.controls.customerName.value.trim() || null,
      customerPhone: this.customerForm.controls.customerPhone.value.trim() || null,
      saleDiscount: { type: this.saleDiscountType(), value: this.saleDiscountValue() },
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
        itemDiscount: { type: item.itemDiscountType as 0 | 1 | 2, value: item.itemDiscountValue },
        hsnCode: item.hsnCode ?? null,
      })),
      rules,
    });

    const configuredRuleByBatchId = new Map(
      rules
        .filter((rule) => rule.ruleType === 'BatchPercentage' && rule.inventoryBatchId && rule.percentage > 0)
        .map((rule) => [rule.inventoryBatchId!, rule])
    );
    const saleLevelEligibleSubtotal = this.roundAmount(
      pricing.lines.reduce((sum, line) => sum + (line.preTaxAmount - line.itemDiscountAmount), 0)
    );

    return {
      totalAmount: pricing.totals.grandTotal,
      totalTaxableAmount: this.roundAmount(pricing.lines.reduce((sum, line) => sum + line.taxableAmount, 0)),
      totalTaxAmount: pricing.totals.totalTax,
      totalDiscountAmount: pricing.totals.totalDiscount,
      saleLevelEligibleSubtotal,
      configuredSaleRule: null,
      lines: pricing.lines.map((line) => {
        const configuredRule = configuredRuleByBatchId.get(line.inventoryBatchId) ?? null;
        const preTaxMargin = Math.max(0, line.preTaxAmount - (line.costPrice * line.quantity));
        const maxAllowedItemDiscountFlat = this.roundAmount(preTaxMargin);
        const maxAllowedItemDiscountPercent = line.preTaxAmount > 0
          ? this.roundAmount((preTaxMargin * 100) / line.preTaxAmount)
          : 0;

        return {
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

  private async onOfflineSubmit(): Promise<void> {
    if (this.isOfflineSubmitting()) return;

    const shopId = this.activeShopId();
    const settings = this.resolveOfflineSettings(shopId);
    if (!settings?.enabled || !settings.deviceId) {
      this.paymentSplitError.set('sales.newSale.offline.blockDeviceNotEnabled');
      return;
    }

    const snapshotCompletedAt = await this.resolveOfflineSnapshotCompletedAt(shopId);
    if (!snapshotCompletedAt) {
      this.paymentSplitError.set('sales.newSale.offline.blockSnapshotStale');
      return;
    }

    const snapshotAgeMs = Date.now() - Date.parse(snapshotCompletedAt);
    if (!Number.isFinite(snapshotAgeMs)) {
      this.paymentSplitError.set('sales.newSale.offline.blockSnapshotStale');
      return;
    }

    if (snapshotAgeMs > MAX_OFFLINE_AGE_MS) {
      this.paymentSplitError.set('sales.newSale.offline.blockSnapshotTooOld');
      return;
    }

    if (!this.hasUsableOfflineInvoiceLease(settings)) {
      this.paymentSplitError.set('sales.newSale.offline.blockInvoiceUnavailable');
      return;
    }

    const hasGrace = await this.authService.canUseOfflineSalesAuthGrace();
    if (!hasGrace) {
      this.paymentSplitError.set('sales.newSale.offline.blockAuthGraceInvalid');
      return;
    }

    const paidAmount = this.toFiniteAmount(this.paymentForm.controls.paidAmount.value);
    const dueAmount = this.toFiniteAmount(this.paymentForm.controls.dueAmount.value);
    const totalAmount = this.roundAmount(this.totalAmount());

    if (
      !Number.isFinite(paidAmount) ||
      !Number.isFinite(dueAmount) ||
      paidAmount < 0 ||
      dueAmount < 0 ||
      !this.areAmountsEqual(paidAmount + dueAmount, totalAmount)
    ) {
      this.paymentSplitError.set('sales.newSale.invalidPaymentSplit');
      return;
    }

    const paymentMethod = this.paymentForm.controls.paymentMethod.value;
    const offlineCustomer = await this.resolveOfflineCustomer(shopId);
    if ((paymentMethod === 4 || dueAmount > 0) && !offlineCustomer) {
      this.paymentSplitError.set('sales.newSale.offline.blockDueRequiresCustomer');
      return;
    }

    this.isOfflineSubmitting.set(true);
    this.offlineSubmitError.set('');
    this.paymentSplitError.set('');

    try {
      const customerName = offlineCustomer?.name ?? null;
      const customerPhone = offlineCustomer?.phoneNumber ?? null;

      const lines: OfflineSalePricingLineInput[] = this.cart().map((item) => ({
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
        itemDiscount: { type: item.itemDiscountType as 0 | 1 | 2, value: item.itemDiscountValue },
        hsnCode: item.hsnCode ?? null,
      }));

      const pricingInput: OfflineSalePricingInput = {
        soldAt: new Date().toISOString(),
        paymentMethod: this.paymentForm.controls.paymentMethod.value,
        paidAmount,
        customerId: offlineCustomer?.customerId ?? null,
        customerName,
        customerPhone,
        saleDiscount: { type: this.saleDiscountType() as 0 | 1 | 2, value: this.saleDiscountValue() },
        lines,
        rules: [],
      };

      const fiscalYear = settings.lastReservedLease?.fiscalYear ?? new Date().getFullYear().toString();
      const request: OfflineFinalizeRequest = {
        shopId,
        deviceId: settings.deviceId,
        fiscalYear,
        pricingInput,
        maxSnapshotAgeMs: MAX_OFFLINE_AGE_MS,
      };

      const result = await this.offlineFinalization.finalizeAndQueue(request);

      if (result.ok) {
        this.updateOfflineInvoiceLeaseCount(shopId, result.remainingInvoiceCount);
        this.offlineConfirmation.set({
          invoiceNumber: result.payload.invoiceNumber,
          grandTotal: result.payload.pricing.totals.grandTotal,
          clientSaleId: result.payload.clientSaleId,
        });
        this.showOfflineConfirmation.set(true);
        this.resetTransientState();
        this.offlineCatalogCache.set(null);
        this.offlineCatalogCacheKey.set(null);
        await this.refreshOfflineState(shopId, settings.deviceId);
      } else {
        const reasonKey: Record<typeof result.reason, string> = {
          SNAPSHOT_STALE: 'sales.newSale.offline.blockSnapshotStale',
          MISSING_CATALOG_ITEM: 'sales.newSale.offline.blockMissingItem',
          INSUFFICIENT_SHADOW_STOCK: 'sales.newSale.offline.blockInsufficientStock',
          MISSING_DUE_CUSTOMER: 'sales.newSale.offline.blockDueRequiresCustomer',
          INVOICE_UNAVAILABLE: 'sales.newSale.offline.blockInvoiceUnavailable',
        };
        this.paymentSplitError.set(reasonKey[result.reason]);
      }
    } finally {
      this.isOfflineSubmitting.set(false);
    }
  }

  private resolveOfflineSettings(shopId: string): OfflineSalesDeviceSettings | null {
    const current = this.deviceSettings();
    if (current?.shopId === shopId) {
      return current;
    }

    const loaded = this.deviceSettingsStorage.loadSettings(shopId);
    this.deviceSettings.set(loaded);
    return loaded;
  }

  private async resolveOfflineSnapshotCompletedAt(shopId: string): Promise<string | null> {
    const current = this.snapshotCompletedAt();
    if (current) {
      return current;
    }

    const snapshotInfo = await this.offlineSnapshotDb.getUsableSnapshotInfo(shopId);
    const completedAt = snapshotInfo?.completedAt ?? null;
    this.snapshotCompletedAt.set(completedAt);
    return completedAt;
  }

  private hasUsableOfflineInvoiceLease(settings: OfflineSalesDeviceSettings): boolean {
    const lease = settings.lastReservedLease;
    if (!lease || lease.remainingCount <= 0) {
      return false;
    }

    const expiresAtMs = Date.parse(lease.expiresAt);
    return Number.isFinite(expiresAtMs) && expiresAtMs > Date.now();
  }

  private async resolveOfflineCustomer(shopId: string): Promise<OfflineCustomerLiteSnapshot | null> {
    const selectedCustomerId = this.selectedCustomerId();
    if (!selectedCustomerId) {
      return null;
    }

    let customers = this.offlineCachedCustomers();
    if (customers.length === 0) {
      customers = await this.offlineSnapshotDb.getUsableCustomers(shopId);
      this.offlineCachedCustomers.set(customers);
    }

    return customers.find((customer) => customer.customerId === selectedCustomerId) ?? null;
  }

  printA4(saleId: string): void {
    window.open(`/sales/${saleId}/print?template=a4`, '_blank');
  }

  printThermal(saleId: string): void {
    window.open(`/sales/${saleId}/print?template=thermal`, '_blank');
  }

  printOfflineA4(): void {
    const confirmation = this.offlineConfirmation();
    if (!confirmation) {
      return;
    }

    window.open(`/sales/${confirmation.clientSaleId}/print?template=a4&offline=1`, '_blank');
  }

  printOfflineThermal(): void {
    const confirmation = this.offlineConfirmation();
    if (!confirmation) {
      return;
    }

    window.open(`/sales/${confirmation.clientSaleId}/print?template=thermal&offline=1`, '_blank');
  }

  private updateOfflineInvoiceLeaseCount(shopId: string, remainingCount: number): void {
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
      this.deviceSettings.set(updated);
    }

    this.offlineInvoiceRemaining.set(remainingCount);
  }
}
