import { SaleCancellationRecordComponent } from './sale-cancellation-record.component';
import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import { DateOnlyPipe } from '../../../shared/pipes/date-only.pipe';
import { SALE_RETURN_CONDITIONS } from '../services/sale.models';
import type {
  InvoiceDeclaration,
  InvoiceDocument,
  InvoiceDocumentLine,
  SaleDto,
  SaleItemDto,
} from '../services/sale.models';
import { ShopDetails } from '../../shops/services/shop.service';
import {
  type TaxSummaryRow,
  declarationKey,
  documentTitleKey,
  groupTaxComponents,
  hasDistinctTradeName,
  lineTaxAmount,
  missingDocumentNoteKey,
  paymentStatusKey,
  redeemedCreditNoteCodes,
  paymentMethodLabelKey,
} from './sale-invoice-document.util';

@Component({
  selector: 'app-sale-invoice-a4',
  standalone: true,
  imports: [SaleCancellationRecordComponent, CommonModule, TranslocoPipe, DateOnlyPipe],
  templateUrl: './sale-invoice-a4.component.html',
  styleUrl: './sale-invoice-a4.component.scss',
})
export class SaleInvoiceA4Component {
  private readonly transloco = inject(TranslocoService);

  @Input() sale!: SaleDto;
  /** The live shop: used only for a sale without an issued document. An issued document carries its own seller. */
  @Input() shop!: ShopDetails;
  @Input() pendingSync = false;

  getCreditNoteSettlementCodes(): string[] {
    return redeemedCreditNoteCodes(this.sale);
  }

  getCreditNoteSettlementLabelKey(): string {
    const codes = this.getCreditNoteSettlementCodes();

    if (codes.length === 0) {
      return 'sales.invoice.creditNoteSettlement';
    }

    return 'sales.invoice.creditNoteSettlementWithCodes';
  }

  getPaymentMethodLabel(method: number | string): string {
    return this.transloco.translate(paymentMethodLabelKey(method));
  }

  getPaymentStatusKey(): string {
    return paymentStatusKey(this.sale);
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

  get shopAddress(): string {
    return [this.shop.address, this.shop.city, this.shop.state, this.shop.pincode]
      .filter(Boolean)
      .join(', ');
  }

  missingDocumentNoteKey(): string | null {
    return missingDocumentNoteKey(this.sale, this.pendingSync);
  }

  documentTitleKey(document: InvoiceDocument): string {
    return documentTitleKey(document);
  }

  hasDistinctTradeName(document: InvoiceDocument): boolean {
    return hasDistinctTradeName(document);
  }

  declarationKey(declaration: InvoiceDeclaration): string {
    return declarationKey(declaration);
  }

  lineTaxAmount(line: InvoiceDocumentLine): number {
    return lineTaxAmount(line);
  }

  taxSummary(document: InvoiceDocument): TaxSummaryRow[] {
    return groupTaxComponents(document.totals.components);
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
