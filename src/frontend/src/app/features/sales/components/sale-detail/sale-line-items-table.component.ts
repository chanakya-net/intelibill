import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { TableModule } from 'primeng/table';
import { SaleItemDto } from '../../services/sale.models';

@Component({
  selector: 'app-sale-line-items-table',
  standalone: true,
  imports: [CommonModule, TableModule, TranslocoPipe],
  templateUrl: './sale-line-items-table.component.html',
})
export class SaleLineItemsTableComponent {
  @Input({ required: true }) items: readonly SaleItemDto[] = [];
  @Input() currency = 'INR';
  @Input() totalTaxAmount: number | null = null;

  protected get inferredTaxIncludedForMissingItems(): boolean {
    const hasItems = this.items.length > 0;
    if (!hasItems) {
      return true;
    }

    const hasMissing = this.items.some((item) => item.isPriceIncludingTax === null || item.isPriceIncludingTax === undefined);
    if (!hasMissing) {
      return true;
    }

    const totalTax = this.totalTaxAmount ?? this.items.reduce((sum, item) => sum + item.taxAmount, 0);
    const includedTaxTotal = this.items.reduce((sum, item) => sum + this.getLineTaxAmountForMode(item, true), 0);
    const excludedTaxTotal = this.items.reduce((sum, item) => sum + this.getLineTaxAmountForMode(item, false), 0);
    const includedDelta = Math.abs(includedTaxTotal - totalTax);
    const excludedDelta = Math.abs(excludedTaxTotal - totalTax);
    return includedDelta <= excludedDelta;
  }

  getUnitSubtotal(item: SaleItemDto): number {
    if (item.taxRatePercent <= 0 || !this.isPriceIncludingTax(item)) {
      return item.salesPrice;
    }

    return item.salesPrice / (1 + item.taxRatePercent / 100);
  }

  getLineTaxAmount(item: SaleItemDto): number {
    return this.getLineTaxAmountForMode(item, this.isPriceIncludingTax(item));
  }

  getLineTotal(item: SaleItemDto): number {
    return item.quantity * item.salesPrice;
  }

  isPriceIncludingTax(item: SaleItemDto): boolean {
    if (typeof item.isPriceIncludingTax === 'boolean') {
      return item.isPriceIncludingTax;
    }

    return this.inferredTaxIncludedForMissingItems;
  }

  isFullyReturned(item: SaleItemDto): boolean {
    return item.returnableQuantity <= 0;
  }

  protected getLineTaxAmountForMode(item: SaleItemDto, isPriceIncludingTax: boolean): number {
    if (item.taxRatePercent <= 0) {
      return 0;
    }

    if (isPriceIncludingTax) {
      return (item.quantity * item.salesPrice * item.taxRatePercent) / (100 + item.taxRatePercent);
    }

    return (item.quantity * item.salesPrice * item.taxRatePercent) / 100;
  }

  hasGoods(): boolean {
    return this.items.some(i => i.lineType === 'Goods');
  }

  hasServices(): boolean {
    return this.items.some(i => i.lineType === 'Service');
  }

  isMixedBill(): boolean {
    return this.hasGoods() && this.hasServices();
  }

  getGoodsItems(): SaleItemDto[] {
    return this.items.filter(i => i.lineType === 'Goods');
  }

  getServiceItems(): SaleItemDto[] {
    return this.items.filter(i => i.lineType === 'Service');
  }
}
