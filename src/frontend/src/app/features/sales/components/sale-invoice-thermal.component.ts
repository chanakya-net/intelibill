import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import type { SaleDto, SaleItemDto } from '../services/sale.models';
import { ShopDetails } from '../../shops/services/shop.service';

@Component({
  selector: 'app-sale-invoice-thermal',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './sale-invoice-thermal.component.html',
  styleUrl: './sale-invoice-thermal.component.scss',
})
export class SaleInvoiceThermalComponent {
  private readonly transloco = inject(TranslocoService);

  @Input() sale!: SaleDto;
  @Input() shop!: ShopDetails;
  @Input() pendingSync = false;

  getCreditNoteSettlementCodes(): string[] {
    return this.sale.returns
      .map((saleReturn) => saleReturn.creditNote?.code ?? '')
      .filter((code): code is string => code.trim().length > 0);
  }

  getCreditNoteSettlementLabelKey(): string {
    const codes = this.getCreditNoteSettlementCodes();

    if (codes.length === 0) {
      return 'sales.invoice.creditNoteSettlement';
    }

    return 'sales.invoice.creditNoteSettlementWithCodes';
  }

  get shopAddress(): string {
    return [this.shop.address, this.shop.city, this.shop.state, this.shop.pincode]
      .filter(Boolean)
      .join(', ');
  }

  getPaymentMethodLabel(method: number): string {
    const map: Record<number, string> = {
      1: 'sales.newSale.paymentMethods.cash',
      2: 'sales.newSale.paymentMethods.upi',
      3: 'sales.newSale.paymentMethods.card',
      4: 'sales.newSale.paymentMethods.credit',
    };
    return this.transloco.translate(map[method] ?? 'shops.unknown');
  }

  getCustomerDisplay(): string {
    if (this.sale.customerName) {
      return this.sale.customerName;
    }

    return this.transloco.translate('sales.history.walkIn');
  }

  getCustomerPhone(): string | null {
    return this.sale.customerPhone;
  }

  getLineDiscountAmount(item: SaleItemDto): number {
    return item.itemDiscountAmount + item.saleDiscountAmount;
  }

  getPaymentStatus(): string {
    if (this.sale.dueAmount === 0) {
      return this.transloco.translate('sales.invoice.paid');
    }

    if (this.sale.paidAmount > 0) {
      return this.transloco.translate('sales.invoice.partiallyPaid');
    }

    return this.transloco.translate('sales.invoice.unpaid');
  }

  hasGoods(): boolean {
    return this.sale.items.some((i) => i.lineType === 'Goods');
  }

  hasServices(): boolean {
    return this.sale.items.some((i) => i.lineType === 'Service');
  }

  isMixedBill(): boolean {
    return this.hasGoods() && this.hasServices();
  }

  getGoodsItems(): SaleItemDto[] {
    return this.sale.items.filter((i) => i.lineType === 'Goods');
  }

  getServiceItems(): SaleItemDto[] {
    return this.sale.items.filter((i) => i.lineType === 'Service');
  }
}
