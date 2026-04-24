import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DividerModule } from 'primeng/divider';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import { AuthService } from '../../../core/auth/auth.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { SalesCartDraftItem, SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import { AvailableBatchDto, InventoryService } from '../../inventory/services/inventory.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { RecordSaleItemRequest, RecordSaleRequest, PAYMENT_METHOD_VALUES } from '../services/sale.service';
import { SalesFacade } from '../state/sales.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';

interface CartItem extends SalesCartDraftItem {}

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
  private readonly salesFacade = inject(SalesFacade);
  private cartLoadToken = 0;

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
  readonly isScannerOpen = signal(false);
  readonly isWalkIn = signal(true);
  readonly paymentSplitError = signal('');

  readonly isSubmitting = this.salesFacade.submitting;
  readonly serverError = this.salesFacade.errorMessage;
  readonly lastMutationSucceeded = this.salesFacade.lastMutationSucceeded;
  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? '');
  readonly cartBootstrapped = signal(false);

  readonly subtotalAmount = computed(() =>
    this.cart().reduce((sum, item) => sum + this.getLineSubtotal(item), 0)
  );
  readonly totalTaxAmount = computed(() =>
    this.cart().reduce((sum, item) => sum + this.getLineTaxAmount(item), 0)
  );
  readonly totalAmount = computed(() =>
    this.cart().reduce((sum, item) => sum + this.getLineTotal(item), 0)
  );
  readonly normalizedCustomerPhone = computed(() => this.customerForm.controls.customerPhone.value.trim());
  readonly canUseCredit = computed(() => !!this.selectedCustomerId() || this.normalizedCustomerPhone().length > 0);
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
    this.salesFacade.clearError();
    this.salesFacade.clearMutationStatus();

    effect(() => {
      if (this.lastMutationSucceeded()) {
        this.cart.set([]);
        this.searchInput.set('');
        this.customerForm.reset();
        this.selectedCustomerId.set(null);
        this.paymentForm.reset({ paymentMethod: 1, paidAmount: 0, dueAmount: 0 });
        this.paymentSplitError.set('');
        this.salesFacade.clearMutationStatus();
        this.router.navigate(['/sales']);
      }
    });

    effect(() => {
      const total = this.totalAmount();
      const dueControl = this.paymentForm.controls.dueAmount;
      const paidControl = this.paymentForm.controls.paidAmount;
      const currentDue = Number(dueControl.value ?? 0);
      const normalizedDue = Math.max(0, Math.min(currentDue, total));
      if (normalizedDue !== currentDue) {
        dueControl.setValue(normalizedDue, { emitEvent: false });
      }

      const paid = Math.max(0, total - normalizedDue);
      if (Number(paidControl.value ?? 0) !== paid) {
        paidControl.setValue(paid, { emitEvent: false });
      }
    });

    effect(() => {
      if (this.canUseCredit()) {
        return;
      }

      if (this.paymentForm.controls.paymentMethod.value === 4) {
        this.paymentForm.controls.paymentMethod.setValue(1);
      }
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
    }));

    const customerName = this.customerForm.controls.customerName.value.trim() || null;
    const customerPhone = this.customerForm.controls.customerPhone.value.trim() || null;
    const paidAmount = Number(this.paymentForm.controls.paidAmount.value ?? 0);
    const dueAmount = Number(this.paymentForm.controls.dueAmount.value ?? 0);
    const totalAmount = this.totalAmount();

    if (paidAmount < 0 || dueAmount < 0 || Number((paidAmount + dueAmount).toFixed(2)) !== Number(totalAmount.toFixed(2))) {
      this.paymentSplitError.set('sales.newSale.invalidPaymentSplit');
      return;
    }

    if (dueAmount > 0 && !this.selectedCustomerId() && !customerPhone) {
      this.paymentSplitError.set('sales.newSale.customerRequiredForDue');
      return;
    }

    if (this.paymentForm.controls.paymentMethod.value === 4 && !this.canUseCredit()) {
      this.paymentSplitError.set('sales.newSale.creditRequiresCustomerPhone');
      return;
    }

    const request: RecordSaleRequest = {
      customerId: this.selectedCustomerId(),
      customerName,
      customerPhone,
      paymentMethod: this.paymentForm.controls.paymentMethod.value,
      paidAmount,
      dueAmount,
      items,
    };

    this.salesFacade.recordSale(request);
  }

  onCancel(): void {
    this.router.navigate(['/sales']);
  }

  private addBatchToCart(batch: AvailableBatchDto, quantityToAdd: number): boolean {
    const barcode = batch.barcode;
    let added = false;

    this.cart.update((items) => {
      const existingIndex = items.findIndex(
        (item) => item.barcode === barcode && item.batchNumber === batch.batchNumber,
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
          barcode,
          itemName: batch.itemName,
          batchNumber: batch.batchNumber,
          quantity: quantityToAdd,
          availableQuantity: batch.quantity,
          salesPrice: batch.salesPrice,
          mrp: batch.mrp,
          taxRatePercent: batch.taxRatePercent,
          taxIncluded: batch.taxIncluded,
          costPrice: 0,
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
}
