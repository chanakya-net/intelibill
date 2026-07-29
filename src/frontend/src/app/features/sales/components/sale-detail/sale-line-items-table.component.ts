import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import type { SaleItemDto } from '../../services/sale.models';

@Component({
  selector: 'app-sale-line-items-table',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './sale-line-items-table.component.html',
  styleUrl: './sale-line-items-table.component.scss',
})
export class SaleLineItemsTableComponent {
  private readonly transloco = inject(TranslocoService);

  @Input({ required: true }) items: readonly SaleItemDto[] = [];
  @Input() currency = 'INR';
  @Input() totalTaxAmount: number | null = null;

  protected get inferredTaxIncludedForMissingItems(): boolean {
    const hasItems = this.items.length > 0;
    if (!hasItems) {
      return true;
    }

    const hasMissing = this.items.some(
      (item) => item.isPriceIncludingTax === null || item.isPriceIncludingTax === undefined,
    );
    if (!hasMissing) {
      return true;
    }

    const totalTax =
      this.totalTaxAmount ?? this.items.reduce((sum, item) => sum + item.taxAmount, 0);
    const includedTaxTotal = this.items.reduce(
      (sum, item) => sum + this.getLineTaxAmountForMode(item, true),
      0,
    );
    const excludedTaxTotal = this.items.reduce(
      (sum, item) => sum + this.getLineTaxAmountForMode(item, false),
      0,
    );
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
    return item.totalAmount;
  }

  getSectionTotal(items: readonly SaleItemDto[]): number {
    return items.reduce((sum, item) => sum + this.getLineTotal(item), 0);
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
    return this.items.some((i) => i.lineType === 'Goods');
  }

  hasServices(): boolean {
    return this.items.some((i) => i.lineType === 'Service');
  }

  isMixedBill(): boolean {
    return this.hasGoods() && this.hasServices();
  }

  getGoodsItems(): SaleItemDto[] {
    return this.items.filter((i) => i.lineType === 'Goods');
  }

  getServiceItems(): SaleItemDto[] {
    return this.items.filter((i) => i.lineType === 'Service');
  }

  getSections(): {
    kind: 'goods' | 'services';
    title: string;
    summary: string;
    items: SaleItemDto[];
  }[] {
    const sections: {
      kind: 'goods' | 'services';
      title: string;
      summary: string;
      items: SaleItemDto[];
    }[] = [];
    const goodsItems = this.getGoodsItems();
    const serviceItems = this.getServiceItems();

    if (goodsItems.length > 0) {
      sections.push({
        kind: 'goods',
        title: this.transloco.translate('sales.detail.goodsSold'),
        summary: this.transloco.translate(
          goodsItems.length === 1 ? 'sales.detail.itemCountOne' : 'sales.detail.itemCountMany',
          { count: goodsItems.length },
        ),
        items: goodsItems,
      });
    }

    if (serviceItems.length > 0) {
      sections.push({
        kind: 'services',
        title: this.transloco.translate('sales.detail.servicesSold'),
        summary: this.transloco.translate(
          serviceItems.length === 1
            ? 'sales.detail.serviceCountOne'
            : 'sales.detail.serviceCountMany',
          { count: serviceItems.length },
        ),
        items: serviceItems,
      });
    }

    return sections;
  }

  getLineCode(item: SaleItemDto): string {
    return item.hsnCode || item.lineCode || '-';
  }

  isReturnable(item: SaleItemDto): boolean {
    return item.returnableQuantity > 0;
  }
}
