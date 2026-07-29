import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import {
  getPaymentMethodLabel,
  type SaleCreditNoteRedemptionSummaryDto,
  type SaleDto,
} from '../../services/sale.models';

@Component({
  selector: 'app-sale-summary-panel',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './sale-summary-panel.component.html',
  styleUrl: './sale-summary-panel.component.scss',
})
export class SaleSummaryPanelComponent {
  private readonly transloco = inject(TranslocoService);

  @Input({ required: true }) sale!: SaleDto;
  @Input() currency = 'INR';

  subtotalAmount(): number {
    return this.sale.totalBeforeDiscount - this.sale.totalTaxAmount;
  }

  hasDiscount(): boolean {
    return this.sale.totalDiscountAmount > 0;
  }

  hasCreditNoteSettlement(): boolean {
    return (this.sale.creditNoteAppliedAmount ?? 0) > 0;
  }

  creditNoteRedemptionSummaries(): readonly SaleCreditNoteRedemptionSummaryDto[] {
    return this.sale.creditNoteRedemptionSummaries ?? [];
  }

  paymentMethodLabel(): string {
    const label = getPaymentMethodLabel(this.sale.paymentMethod).toLowerCase();
    return this.transloco.translate(
      ['cash', 'upi', 'card', 'credit'].includes(label)
        ? `sales.newSale.paymentMethods.${label}`
        : 'shops.unknown',
    );
  }

  paymentStatusLabel(): string {
    if (this.sale.dueAmount === 0) {
      return this.transloco.translate('sales.invoice.paid');
    }

    return this.transloco.translate(
      this.sale.paidAmount > 0 ? 'sales.invoice.partiallyPaid' : 'sales.invoice.unpaid',
    );
  }

  customerName(): string {
    return this.sale.customerName || this.transloco.translate('sales.invoice.walkInCustomer');
  }

  customerPhone(): string {
    return this.sale.customerPhone || this.transloco.translate('sales.detail.notProvided');
  }
}
