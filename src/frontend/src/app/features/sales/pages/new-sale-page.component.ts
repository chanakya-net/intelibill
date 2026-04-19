import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

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

import { AvailableBatchDto, InventoryService } from '../../inventory/services/inventory.service';
import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { RecordSaleItemRequest, RecordSaleRequest, PAYMENT_METHOD_VALUES } from '../services/sale.service';
import { SalesFacade } from '../state/sales.facade';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';

interface CartItem {
  barcode: string;
  itemName: string;
  batchNumber: string;
  quantity: number;
  availableQuantity: number;
  salesPrice: number;
  mrp: number;
  taxRatePercent: number;
  taxIncluded: boolean;
  costPrice: number;
}

@Component({
  selector: 'app-new-sale-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    BarcodeScannerDialogComponent,
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
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly inventoryService = inject(InventoryService);
  private readonly salesFacade = inject(SalesFacade);

  readonly paymentMethods = PAYMENT_METHOD_VALUES;
  readonly cart = signal<CartItem[]>([]);
  readonly searchInput = signal('');
  readonly isSearchingBatches = signal(false);
  readonly batchSearchError = signal('');
  readonly availableBatches = signal<AvailableBatchDto[]>([]);
  readonly showBatchPicker = signal(false);
  readonly selectedBatch = signal<AvailableBatchDto | null>(null);
  readonly isScannerOpen = signal(false);
  readonly isWalkIn = signal(true);

  readonly isSubmitting = this.salesFacade.submitting;
  readonly serverError = this.salesFacade.errorMessage;
  readonly lastMutationSucceeded = this.salesFacade.lastMutationSucceeded;

  readonly totalAmount = computed(() =>
    this.cart().reduce((sum, item) => sum + item.salesPrice * item.quantity, 0)
  );
  readonly totalTaxAmount = computed(() =>
    this.cart().reduce((sum, item) => {
      const lineTotal = item.salesPrice * item.quantity;
      if (item.taxIncluded) {
        return sum + lineTotal - lineTotal / (1 + item.taxRatePercent / 100);
      }
      return sum + (lineTotal * item.taxRatePercent) / 100;
    }, 0)
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
  });

  constructor() {
    this.salesFacade.clearError();
    this.salesFacade.clearMutationStatus();

    effect(() => {
      if (this.lastMutationSucceeded()) {
        this.cart.set([]);
        this.searchInput.set('');
        this.customerForm.reset();
        this.paymentForm.reset({ paymentMethod: 1 });
        this.salesFacade.clearMutationStatus();
        this.router.navigate(['/sales']);
      }
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

  onSubmit(): void {
    if (this.isSubmitting()) return;
    if (this.cart().length === 0) return;
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
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
    }));

    const customerName = this.customerForm.controls.customerName.value.trim() || null;
    const customerPhone = this.customerForm.controls.customerPhone.value.trim() || null;

    const request: RecordSaleRequest = {
      customerId: null,
      customerName,
      customerPhone,
      paymentMethod: this.paymentForm.controls.paymentMethod.value,
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
}
