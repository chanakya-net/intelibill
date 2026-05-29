import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { SALE_RETURN_CONDITIONS } from '../services/sale.models';
import type { SaleDto, SaleItemDto } from '../services/sale.models';
import { ShopDetails } from '../../shops/services/shop.service';

@Component({
  selector: 'app-sale-invoice-a4',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './sale-invoice-a4.component.html',
  styleUrl: './sale-invoice-a4.component.scss',
})
export class SaleInvoiceA4Component {
  @Input() sale!: SaleDto;
  @Input() shop!: ShopDetails;
  @Input() pendingSync = false;

  getPaymentMethodLabel(method: number): string {
    const map: Record<number, string> = { 1: 'Cash', 2: 'UPI', 3: 'Card', 4: 'Credit' };
    return map[method] ?? 'Unknown';
  }

  getReturnConditionLabel(condition: 1 | 2): string {
    const condition_map = SALE_RETURN_CONDITIONS.find((c) => c.value === condition);
    return condition_map?.label ?? 'Unknown';
  }

  getCustomerDisplay(): string {
    if (this.sale.customerId) {
      return this.sale.customerName || 'Customer';
    }
    return 'Walk-in';
  }

  getCustomerPhone(): string | null {
    return this.sale.customerId ? this.sale.customerPhone : null;
  }

  hasGoods(): boolean {
    return this.sale.items.some(i => i.lineType === 'Goods');
  }

  hasServices(): boolean {
    return this.sale.items.some(i => i.lineType === 'Service');
  }

  isMixedBill(): boolean {
    return this.hasGoods() && this.hasServices();
  }

  getGoodsItems(): SaleItemDto[] {
    return this.sale.items.filter(i => i.lineType === 'Goods');
  }

  getServiceItems(): SaleItemDto[] {
    return this.sale.items.filter(i => i.lineType === 'Service');
  }
}
