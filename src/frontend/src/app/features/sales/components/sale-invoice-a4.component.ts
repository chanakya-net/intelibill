import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

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

  getPaymentMethodLabel(method: number): string {
    const map: Record<number, string> = {
      1: 'sales.newSale.paymentMethods.cash',
      2: 'sales.newSale.paymentMethods.upi',
      3: 'sales.newSale.paymentMethods.card',
      4: 'sales.newSale.paymentMethods.credit',
    };
    return this.transloco.translate(map[method] ?? 'shops.unknown');
  }

  getReturnConditionLabel(condition: 1 | 2 | null): string {
    if (condition === null) {
      return this.transloco.translate('sales.returns.preview.refundOnlyService');
    }

    const condition_map = SALE_RETURN_CONDITIONS.find((c) => c.value === condition);
    return this.transloco.translate(condition_map?.labelKey ?? 'shops.unknown');
  }

  getCustomerDisplay(): string {
    if (this.sale.customerId) {
      return this.sale.customerName || this.transloco.translate('sales.invoice.customer');
    }
    return this.transloco.translate('sales.history.walkIn');
  }

  getCustomerPhone(): string | null {
    return this.sale.customerId ? this.sale.customerPhone : null;
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
