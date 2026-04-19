import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { DividerModule } from 'primeng/divider';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import { SalesFacade } from '../state/sales.facade';
import { SaleItemDto } from '../services/sale.service';

@Component({
  selector: 'app-sale-detail-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    DialogModule,
    DividerModule,
    ProgressSpinnerModule,
    TagModule,
    TableModule,
    TranslocoPipe,
  ],
  templateUrl: './sale-detail-overlay.component.html',
})
export class SaleDetailOverlayComponent {
  private readonly salesFacade = inject(SalesFacade);

  @Input() visible = false;
  @Output() visibleChange = new EventEmitter<boolean>();

  readonly sale = this.salesFacade.selectedSale;
  readonly isLoading = this.salesFacade.loadingSaleDetail;
  readonly inferredTaxIncludedForMissingItems = computed(() => {
    const detail = this.sale();
    if (!detail || detail.items.length === 0) {
      return true;
    }

    const hasMissing = detail.items.some((item) => item.isPriceIncludingTax === undefined || item.isPriceIncludingTax === null);
    if (!hasMissing) {
      return true;
    }

    const includedTaxTotal = detail.items
      .reduce((sum, item) => sum + this.getLineTaxAmountForMode(item, true), 0);
    const excludedTaxTotal = detail.items
      .reduce((sum, item) => sum + this.getLineTaxAmountForMode(item, false), 0);

    const includedDelta = Math.abs(includedTaxTotal - detail.totalTaxAmount);
    const excludedDelta = Math.abs(excludedTaxTotal - detail.totalTaxAmount);
    return includedDelta <= excludedDelta;
  });

  getUnitSubtotal(item: SaleItemDto): number {
    if (item.taxRatePercent <= 0) {
      return item.salesPrice;
    }

    if (!this.isPriceIncludingTax(item)) {
      return item.salesPrice;
    }

    return item.salesPrice / (1 + item.taxRatePercent / 100);
  }

  getLineTaxAmount(item: SaleItemDto): number {
    return this.getLineTaxAmountForMode(item, this.isPriceIncludingTax(item));
  }

  getLineTotal(item: SaleItemDto): number {
    // Match backend sale aggregation logic: total amount always qty * salesPrice.
    return item.quantity * item.salesPrice;
  }

  isPriceIncludingTax(item: SaleItemDto): boolean {
    if (typeof item.isPriceIncludingTax === 'boolean') {
      return item.isPriceIncludingTax;
    }

    return this.inferredTaxIncludedForMissingItems();
  }

  private getLineTaxAmountForMode(item: SaleItemDto, isPriceIncludingTax: boolean): number {
    if (item.taxRatePercent <= 0) {
      return 0;
    }

    if (isPriceIncludingTax) {
      return item.quantity * item.salesPrice * item.taxRatePercent / (100 + item.taxRatePercent);
    }

    return item.quantity * item.salesPrice * item.taxRatePercent / 100;
  }

  subtotalAmount(): number {
    const detail = this.sale();
    if (!detail) {
      return 0;
    }

    return detail.totalAmount - detail.totalTaxAmount;
  }

  totalPrice(): number {
    const detail = this.sale();
    if (!detail) {
      return 0;
    }

    return this.subtotalAmount() + detail.totalTaxAmount;
  }

  onClose(): void {
    this.visibleChange.emit(false);
  }

  paymentMethodLabel(method: number): string {
    const map: Record<number, string> = { 1: 'Cash', 2: 'UPI', 3: 'Card', 4: 'Credit' };
    return map[method] ?? 'Unknown';
  }

  paymentMethodSeverity(method: number): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const map: Record<number, 'success' | 'info' | 'warn' | 'danger'> = { 1: 'success', 2: 'info', 3: 'warn', 4: 'danger' };
    return map[method] ?? 'secondary';
  }
}
