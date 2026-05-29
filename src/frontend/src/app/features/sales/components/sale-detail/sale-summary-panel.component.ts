import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { getPaymentMethodLabel, type SaleDto } from '../../services/sale.models';

@Component({
  selector: 'app-sale-summary-panel',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './sale-summary-panel.component.html',
  styleUrl: './sale-summary-panel.component.scss',
})
export class SaleSummaryPanelComponent {
  @Input({ required: true }) sale!: SaleDto;
  @Input() currency = 'INR';

  subtotalAmount(): number {
    return this.sale.totalBeforeDiscount - this.sale.totalTaxAmount;
  }

  hasDiscount(): boolean {
    return this.sale.totalDiscountAmount > 0;
  }

  paymentMethodLabel(): string {
    return getPaymentMethodLabel(this.sale.paymentMethod);
  }

  customerName(): string {
    return this.sale.customerName || 'Walk-in Customer';
  }

  customerPhone(): string {
    return this.sale.customerPhone || 'Not provided';
  }
}
