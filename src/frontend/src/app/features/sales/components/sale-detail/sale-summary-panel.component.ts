import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { TagModule } from 'primeng/tag';
import { getPaymentMethodLabel, getPaymentMethodSeverity, SaleDto } from '../../services/sale.models';

@Component({
  selector: 'app-sale-summary-panel',
  standalone: true,
  imports: [CommonModule, TagModule, TranslocoPipe],
  templateUrl: './sale-summary-panel.component.html',
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

  paymentMethodSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    return getPaymentMethodSeverity(this.sale.paymentMethod);
  }
}
