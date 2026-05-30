import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DividerModule } from 'primeng/divider';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';

import { BarcodeDetection } from '../../../core/services/barcode-detector.service';
import { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { BarcodeScannerDialogComponent } from '../../../shared/components/barcode-scanner-dialog.component';
import { BatchPickerDialogComponent } from '../components/new-sale/batch-picker-dialog.component';
import { BatchSearchBarComponent } from '../components/new-sale/batch-search-bar.component';
import { CartCheckoutSummaryComponent } from '../components/new-sale/cart-checkout-summary.component';
import {
  CartLineNumberEvent,
  CartLineTextEvent,
  CartQuantityChangedEvent,
  CartTableComponent,
} from '../components/new-sale/cart-table.component';
import { OfflineStatusBannerComponent } from '../components/new-sale/offline-status-banner.component';
import { SaleConfirmationDialogComponent } from '../components/new-sale/sale-confirmation-dialog.component';
import { CustomerDto, SaleCustomerSectionComponent } from '../components/new-sale/sale-customer-section.component';
import { SalePaymentSectionComponent } from '../components/new-sale/sale-payment-section.component';
import { PaymentMethod, SellableDto } from '../services/sale.models';
import { NewSalePageCoordinatorService } from './new-sale-page.coordinator.service';

@Component({
  selector: 'app-new-sale-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    BarcodeScannerDialogComponent,
    ButtonModule,
    CardModule,
    DividerModule,
    InputGroupAddonModule,
    InputGroupModule,
    TranslocoPipe,
    BatchSearchBarComponent,
    BatchPickerDialogComponent,
    CartCheckoutSummaryComponent,
    CartTableComponent,
    OfflineStatusBannerComponent,
    SaleCustomerSectionComponent,
    SalePaymentSectionComponent,
    SaleConfirmationDialogComponent,
  ],
  providers: [NewSalePageCoordinatorService],
  templateUrl: './new-sale-page.component.html',
  styleUrl: './new-sale-page.component.scss',
})
export class NewSalePageComponent implements OnInit, OnDestroy {
  readonly vm = inject(NewSalePageCoordinatorService);

  ngOnInit(): void {
    this.vm.onInit();
  }

  ngOnDestroy(): void {
    this.vm.onDestroy();
  }

  onBatchSearchTermChanged(value: string): void {
    this.vm.onBatchSearchTermChanged(value);
  }

  onBatchSearchSuggestionFilter(value: string): void {
    this.vm.onBatchSearchSuggestionFilter(value);
  }

  onSearchSuggestionSelected(value: string): void {
    this.vm.onSearchSuggestionSelected(value);
  }

  onBatchSearchRequested(value: string): void {
    this.vm.onBatchSearchRequested(value);
  }

  onOpenBatchPicker(): void {
    this.vm.onOpenBatchPicker();
  }

  openScanner(): void {
    this.vm.openScanner();
  }

  onBatchQuantityChanged(quantity: number | null): void {
    this.vm.onBatchQuantityChanged(quantity);
  }

  onBatchPickerBatchSelected(batch: SellableDto): void {
    this.vm.onBatchPickerBatchSelected(batch);
  }

  onQuickProductTileSelected(batch: AvailableBatchDto): void {
    this.vm.onQuickProductTileSelected(batch);
  }

  onBatchPickerClosed(): void {
    this.vm.onBatchPickerClosed();
  }

  onScannerVisibilityChange(visible: boolean): void {
    this.vm.onScannerVisibilityChange(visible);
  }

  onScannedBarcode(detection: BarcodeDetection): void {
    this.vm.onScannedBarcode(detection);
  }

  onClearCart(): void {
    this.vm.onClearCart();
  }

  onCartTableQuantityChanged(event: CartQuantityChangedEvent): void {
    this.vm.onCartTableQuantityChanged(event);
  }

  onCartTableItemRemoved(itemId: string): void {
    this.vm.onCartTableItemRemoved(itemId);
  }

  onCartTableServiceUnitPriceChanged(event: CartLineNumberEvent): void {
    this.vm.onCartTableServiceUnitPriceChanged(event);
  }

  onCartTableHsnCodeChange(event: CartLineTextEvent): void {
    this.vm.onCartTableHsnCodeChange(event);
  }

  onCartTableTaxRateChange(event: CartLineNumberEvent): void {
    this.vm.onCartTableTaxRateChange(event);
  }

  onCartTableDiscountTypeChange(event: CartLineNumberEvent): void {
    this.vm.onCartTableDiscountTypeChange(event);
  }

  onCartTableDiscountValueChange(event: CartLineTextEvent): void {
    this.vm.onCartTableDiscountValueChange(event);
  }

  onCartTableLineDiscountEditorToggled(itemId: string): void {
    this.vm.onCartTableLineDiscountEditorToggled(itemId);
  }

  onCustomerSectionSelected(customer: CustomerDto | null): void {
    this.vm.onCustomerSectionSelected(customer);
  }

  onCustomerSectionSearchCustomers(query: string): void {
    this.vm.onCustomerSectionSearchCustomers(query);
  }

  onPaymentMethodChanged(method: PaymentMethod): void {
    this.vm.onPaymentMethodChanged(method);
  }

  onPaymentPaidAmountChanged(value: number | null): void {
    this.vm.onPaymentPaidAmountChanged(value);
  }

  onPaymentDueAmountChanged(value: number | null): void {
    this.vm.onPaymentDueAmountChanged(value);
  }

  onPaymentSubmitRequested(): void {
    this.vm.onPaymentSubmitRequested();
  }

  onSaleDiscountTypeChange(type: 0 | 1 | 2): void {
    this.vm.onSaleDiscountTypeChange(type);
  }

  onSaleDiscountValueChange(value: number | null | undefined): void {
    this.vm.onSaleDiscountValueChange(value);
  }
}
