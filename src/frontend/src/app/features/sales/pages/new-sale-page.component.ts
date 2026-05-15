import { CommonModule } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { Observable, Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';

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
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { ShopUpdatesSignalRService } from '../../../core/services/shop-updates-signalr.service';
import { SalesCartDraftItem, SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import { Customer } from '../../customers/services/customer.service';
import { CustomersFacade } from '../../customers/state/customers.facade';
import { AvailableBatchDto, InventoryService } from '../../inventory/services/inventory.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import {
  InstantDiscountRequest,
  NO_DISCOUNT,
  PreviewSaleRequest,
  RecordSaleItemRequest,
  RecordSaleRequest,
  SalePreviewDto,
  SalePreviewLineDto,
  PAYMENT_METHOD_VALUES,
  SaleService,
} from '../services/sale.service';
import { SalesFacade } from '../state/sales.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import { DateOnlyPipe } from '../../../shared/pipes/date-only.pipe';

interface CartItem extends SalesCartDraftItem {}

type PreviewRequestResult =
  | { readonly requestId: number; readonly preview: SalePreviewDto; readonly failed?: false }
  | { readonly requestId: number; readonly preview: null; readonly failed: true };

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
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly catalogSync = inject(ProductCatalogSyncService);
  private readonly inventoryService = inject(InventoryService);
  private readonly cartStorage = inject(SalesCartIndexedDbService);
  private readonly customersFacade = inject(CustomersFacade);
  private readonly salesFacade = inject(SalesFacade);
  private readonly saleService = inject(SaleService);
  private readonly shopUpdatesService = inject(ShopUpdatesSignalRService);
  private readonly destroyRef = inject(DestroyRef);
  private cartLoadToken = 0;
  private readonly previewTrigger$ = new Subject<void>();
  private readonly serverUpdateTrigger$ = new Subject<void>();

  readonly paymentMethods = PAYMENT_METHOD_VALUES;
  readonly cart = signal<CartItem[]>([]);
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

  readonly checkoutPreview = signal<SalePreviewDto | null>(null);
  readonly isPreviewLoading = signal(false);
  readonly previewError = signal('');

  // Discounts (cashier instant override). Configured discounts come from preview DTO.
  readonly isSaleDiscountEditorOpen = signal(false);
  readonly saleDiscountType = signal<0 | 1 | 2>(0);
  readonly saleDiscountValue = signal(0);
  readonly saleDiscountError = signal('');

  readonly openLineDiscountEditorByKey = signal<Record<string, boolean>>({});
  readonly cartItemDiscountErrorByKey = signal<Record<string, string>>({});

  // Shop realtime updates
  readonly highlightedRowKeys = signal<Set<string>>(new Set());
  readonly showUpdateNotification = signal(false);
  readonly updateNotificationText = signal('');
  readonly showConfirmation = signal(false);
  private readonly previewRequestState = { latestRequestId: 0 };

  readonly isSubmitting = this.salesFacade.submitting;
  readonly serverError = this.salesFacade.errorMessage;
  readonly lastMutationSucceeded = this.salesFacade.lastMutationSucceeded;
  readonly lastMutationType = this.salesFacade.lastMutationType;
  readonly lastRecordedSale = this.salesFacade.lastRecordedSale;
  readonly customers = this.customersFacade.allCustomers;
  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly cartBootstrapped = signal(false);

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
  readonly paymentMethodsForSelection = computed(() =>
    this.paymentMethods.filter((method) => method.value !== 4 || this.canUseCredit())
  );

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
          this.enforceNoCustomerCreditRestrictions();
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

    // Wire debounced preview trigger (300ms for local edits per issue #234)
    this.previewTrigger$
      .pipe(
        debounceTime(300),
        switchMap((): Observable<PreviewRequestResult | null> => {
          const cart = this.cart();
          if (cart.length === 0) {
            this.clearPreviewState();
            return of(null);
          }
          const requestId = this.beginPreviewRequest();
          const request = this.buildPreviewRequest(cart);
          return this.saleService.previewSale(request).pipe(
            map((preview) => ({ requestId, preview } as PreviewRequestResult)),
            catchError(() => of({ requestId, preview: null, failed: true } as PreviewRequestResult)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result: PreviewRequestResult | null) => {
        if (result === null) {
          return;
        }

        if (result.preview === null) {
          this.finishPreviewRequest(result.requestId, null, !!result.failed);
          return;
        }

        this.finishPreviewRequest(result.requestId, result.preview);
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
        this.serverUpdateTrigger$.next();
      });

    // Handle server update trigger with immediate (non-debounced) refresh
    this.serverUpdateTrigger$
      .pipe(
        switchMap((): Observable<PreviewRequestResult | null> => {
          const cart = this.cart();
          if (cart.length === 0) {
            this.clearPreviewState();
            return of(null);
          }
          const requestId = this.beginPreviewRequest();
          const request = this.buildPreviewRequest(cart);
          return this.saleService.previewSale(request).pipe(
            map((preview) => ({ requestId, preview } as PreviewRequestResult)),
            catchError(() => of({ requestId, preview: null, failed: true } as PreviewRequestResult)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result: PreviewRequestResult | null) => {
        if (result === null) {
          return;
        }

        if (result.preview === null) {
          this.finishPreviewRequest(result.requestId, null, !!result.failed);
          return;
        }

        const oldPreview = this.checkoutPreview();
        this.finishPreviewRequest(result.requestId, result.preview, false, oldPreview);
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

      if (this.canUseCredit()) {
        if (dueControl.disabled) {
          dueControl.enable({ emitEvent: false });
        }
        return;
      }

      this.enforceNoCustomerCreditRestrictions();
    });

    effect(() => {
      const shopId = this.activeShopId();
      const token = ++this.cartLoadToken;
      this.cartBootstrapped.set(false);

      if (!shopId) {
        this.cart.set([]);
        this.cartBootstrapped.set(true);
        return;
      }

      void this.loadPersistedCart(shopId, token);
    });

    effect(() => {
      const shopId = this.activeShopId();
      const cart = this.cart();
      if (!shopId || !this.cartBootstrapped()) {
        return;
      }

      void this.persistCart(shopId, cart);
    });

    // Schedule preview whenever cart changes (after bootstrap). Item-level discount
    // edits always go through cart mutations, so they reach this effect automatically.
    effect(() => {
      const cart = this.cart();
      if (!this.cartBootstrapped()) {
        return;
      }
      if (cart.length === 0) {
        this.checkoutPreview.set(null);
        this.isPreviewLoading.set(false);
        this.previewError.set('');
        return;
      }
      this.previewTrigger$.next();
    });
  }

  onBarcodeSearch(): void {
    const searchTerm = this.searchInput().trim();
    if (!searchTerm) return;

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
          if (this.addBatchToCart(list[0], 1)) {
            this.resetSearchAndPickerState();
          }
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
    this.onBarcodeSearch();
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

    if (!this.addBatchToCart(batch, qty)) {
      this.batchPickerForm.controls.quantity.setErrors({ exceedsStock: true });
      return;
    }

    this.resetSearchAndPickerState();
  }

  onIncreaseCartItem(index: number): void {
    this.cart.update((items) =>
      items.map((item, i) =>
        i === index && item.quantity < item.availableQuantity
          ? { ...item, quantity: item.quantity + 1 }
          : item
      )
    );
  }

  onDecreaseCartItem(index: number): void {
    this.cart.update((items) =>
      items.map((item, i) =>
        i === index && item.quantity > 1
          ? { ...item, quantity: item.quantity - 1 }
          : item
      )
    );
  }

  canIncreaseCartItem(item: CartItem): boolean {
    return item.quantity < item.availableQuantity;
  }

  onRemoveCartItem(index: number): void {
    this.cart.update((items) => items.filter((_, i) => i !== index));
  }

  onClearCart(): void {
    this.cart.set([]);
  }

  hasTax(item: CartItem): boolean {
    return item.taxRatePercent > 0;
  }

  getLineSubtotal(item: CartItem): number {
    return this.getUnitSubtotal(item) * item.quantity;
  }

  getLineTaxAmount(item: CartItem): number {
    return this.getUnitTaxAmount(item) * item.quantity;
  }

  getLineTotal(item: CartItem): number {
    return this.getUnitFinalPrice(item) * item.quantity;
  }

  getUnitSubtotal(item: CartItem): number {
    if (!this.hasTax(item)) {
      return item.salesPrice;
    }

    if (!item.taxIncluded) {
      return item.salesPrice;
    }

    return item.salesPrice / (1 + item.taxRatePercent / 100);
  }

  getUnitTaxAmount(item: CartItem): number {
    if (!this.hasTax(item)) {
      return 0;
    }

    const basePrice = this.getUnitSubtotal(item);
    return (basePrice * item.taxRatePercent) / 100;
  }

  getUnitFinalPrice(item: CartItem): number {
    if (!this.hasTax(item)) {
      return item.salesPrice;
    }

    if (item.taxIncluded) {
      return item.salesPrice;
    }

    return item.salesPrice + this.getUnitTaxAmount(item);
  }

  onSubmit(): void {
    if (this.isSubmitting()) return;
    if (this.cart().length === 0) return;
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    this.paymentSplitError.set('');

    if (this.checkoutPreview() === null) {
      this.paymentSplitError.set('sales.newSale.previewRequired');
      return;
    }

    if (this.saleDiscountError() || Object.values(this.cartItemDiscountErrorByKey()).some((v) => !!v)) {
      this.paymentSplitError.set('sales.newSale.discounts.fixErrors');
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

  private buildPreviewRequest(cart: readonly CartItem[]): PreviewSaleRequest {
    return {
      saleDiscount: { type: this.saleDiscountType(), value: this.saleDiscountValue() },
      items: cart.map((item) => ({
        inventoryBatchId: item.inventoryBatchId,
        barcode: item.barcode,
        batchNumber: item.batchNumber,
        itemName: item.itemName,
        quantity: item.quantity,
        costPrice: item.costPrice,
        salesPrice: item.salesPrice,
        mrp: item.mrp,
        taxRatePercent: item.taxRatePercent,
        isPriceIncludingTax: item.taxIncluded,
        itemDiscount: { type: item.itemDiscountType as 0 | 1 | 2, value: item.itemDiscountValue },
        clientLineKey: item.clientLineKey,
      })),
    };
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

  private addBatchToCart(batch: AvailableBatchDto, quantityToAdd: number): boolean {
    const barcode = batch.barcode;
    let added = false;

    this.cart.update((items) => {
      const existingIndex = items.findIndex(
        (item) => item.inventoryBatchId === batch.inventoryBatchId,
      );

      if (existingIndex >= 0) {
        const existing = items[existingIndex];
        const maxAvailable = Math.max(existing.availableQuantity, batch.quantity);
        const nextQuantity = existing.quantity + quantityToAdd;
        if (nextQuantity > maxAvailable) {
          return items;
        }

        added = true;
        return items.map((item, index) =>
          index === existingIndex
            ? {
                ...item,
                quantity: nextQuantity,
                availableQuantity: maxAvailable,
              }
            : item,
        );
      }

      if (quantityToAdd > batch.quantity) {
        return items;
      }

      added = true;
      return [
        ...items,
        {
          clientLineKey: crypto.randomUUID(),
          barcode,
          itemName: batch.itemName,
          batchNumber: batch.batchNumber,
          inventoryBatchId: batch.inventoryBatchId,
          quantity: quantityToAdd,
          availableQuantity: batch.quantity,
          salesPrice: batch.salesPrice,
          mrp: batch.mrp,
          taxRatePercent: batch.taxRatePercent,
          taxIncluded: batch.taxIncluded,
          costPrice: 0,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ];
    });

    if (!added) {
      this.batchSearchError.set('sales.newSale.exceedsStock');
      return false;
    }

    return true;
  }

  private resetSearchAndPickerState(): void {
    this.showBatchPicker.set(false);
    this.selectedBatch.set(null);
    this.searchInput.set('');
    this.batchPickerForm.reset({ batchNumber: '', quantity: 1 });
    this.availableBatches.set([]);
    this.batchSearchError.set('');
  }

  private async loadPersistedCart(shopId: string, token: number): Promise<void> {
    try {
      const rows = await this.cartStorage.loadCart(shopId, this.cartRetentionMs);
      if (token !== this.cartLoadToken) {
        return;
      }
      this.cart.set([...rows]);
    } catch {
      if (token === this.cartLoadToken) {
        this.cart.set([]);
      }
    } finally {
      if (token === this.cartLoadToken) {
        this.cartBootstrapped.set(true);
      }
    }
  }

  private async persistCart(shopId: string, cart: readonly CartItem[]): Promise<void> {
    try {
      if (cart.length === 0) {
        await this.cartStorage.clearCart(shopId);
        return;
      }

      await this.cartStorage.saveCart(shopId, cart);
    } catch {
      // Ignore persistence errors; cart remains in memory.
    }
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

  private beginPreviewRequest(): number {
    const requestId = ++this.previewRequestState.latestRequestId;
    this.isPreviewLoading.set(true);
    this.previewError.set('');
    return requestId;
  }

  private finishPreviewRequest(
    requestId: number,
    preview: SalePreviewDto | null,
    failed = false,
    oldPreview: SalePreviewDto | null = this.checkoutPreview()
  ): void {
    if (requestId !== this.previewRequestState.latestRequestId) {
      return;
    }

    if (preview === null) {
      this.isPreviewLoading.set(false);
      this.previewError.set(failed ? 'sales.newSale.previewError' : '');
      if (failed) {
        this.checkoutPreview.set(null);
      }
      return;
    }

    this.detectAndHighlightChangedRows(oldPreview, preview);
    this.checkoutPreview.set(preview);
    this.revalidateDiscountsAgainstPreview();
    this.isPreviewLoading.set(false);
  }

  private clearPreviewState(): void {
    this.previewRequestState.latestRequestId += 1;
    this.checkoutPreview.set(null);
    this.isPreviewLoading.set(false);
    this.previewError.set('');
  }

  private resetTransientState(): void {
    this.cart.set([]);
    this.searchInput.set('');
    this.customerNameSuggestions.set([]);
    this.customerForm.reset();
    this.selectedCustomerId.set(null);
    this.selectedCustomerName.set(null);
    this.paymentForm.reset({ paymentMethod: 1, paidAmount: 0, dueAmount: 0 });
    this.paymentSplitError.set('');
    this.checkoutPreview.set(null);
    this.previewError.set('');
  }

  onDone(): void {
    this.showConfirmation.set(false);
    this.salesFacade.clearLastRecordedSale();
    this.salesFacade.clearMutationStatus();
    this.router.navigate(['/sales']);
  }

  printA4(saleId: string): void {
    window.open(`/sales/${saleId}/print?template=a4`, '_blank');
  }

  printThermal(saleId: string): void {
    window.open(`/sales/${saleId}/print?template=thermal`, '_blank');
  }
}
