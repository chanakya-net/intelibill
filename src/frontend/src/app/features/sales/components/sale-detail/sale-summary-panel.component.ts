import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { TagModule } from 'primeng/tag';
import { SaleDto } from '../../services/sale.models';

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
    return this.sale.totalAmount - this.sale.totalTaxAmount;
  }

  hasDiscount(): boolean {
    return this.sale.totalDiscountAmount > 0;
  }

  paymentMethodLabel(): string {
    const map: Record<number, string> = { 1: 'Cash', 2: 'UPI', 3: 'Card', 4: 'Credit' };
    return map[this.sale.paymentMethod] ?? 'Unknown';
  }

  paymentMethodSeverity(): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const map: Record<number, 'success' | 'info' | 'warn' | 'danger'> = { 1: 'success', 2: 'info', 3: 'warn', 4: 'danger' };
    return map[this.sale.paymentMethod] ?? 'secondary';
  }
}
