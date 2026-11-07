import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import type { SaleCancellationDto } from '../services/sale-cancellation.models';
/** A separate commercial reference; the original issued document is never rewritten. */
@Component({
  selector: 'app-sale-cancellation-record',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  template: `
    @if (cancellation; as c) {
      <section
        class="sale-cancellation-record"
        style="border:2px solid currentColor;padding:12px;margin:12px 0;overflow-wrap:anywhere;break-inside:avoid;font-size:inherit;line-height:1.5"
      >
        <strong>{{ 'sales.cancellation.cancelled' | transloco }} · {{ c.reference }}</strong>
        <p>
          {{ 'sales.cancellation.original' | transloco }}: {{ c.originalInvoiceNumber }} ·
          {{ c.originalBusinessDate }}
        </p>
        <p>
          {{ 'sales.cancellation.postingDate' | transloco }}: {{ c.businessDate }} ·
          {{ c.cancelledAt | date: 'dd MMM yyyy, h:mm a' }} ({{ c.businessTimeZoneId }})
        </p>
        <p>{{ c.seller.legalName }} · {{ c.seller.address }}</p>
        <p>{{ c.buyer.name }}</p>
        <p>{{ 'sales.cancellation.reasonLabel' | transloco }}: {{ c.reason }}</p>
        @for (line of c.lines; track line.id) {
          <p>{{ line.description }} · {{ line.quantity | number: '1.0-3' }} {{ line.uqc }}</p>
        }
        <p>{{ 'sales.cancellation.total' | transloco }}: {{ c.totalAmount | currency: 'INR' }}</p>
        <p>
          {{ 'sales.cancellation.dueReduction' | transloco }}:
          {{ c.dueReductionAmount | currency: 'INR' }}
        </p>
        @if (c.cashRefundAmount > 0) {
          <p>
            {{ 'sales.cancellation.cashRefund' | transloco }}:
            {{ c.cashRefundAmount | currency: 'INR' }}
          </p>
        }
        <p>
          {{ disclaimer(c) }}
        </p>
      </section>
    }
  `,
})
export class SaleCancellationRecordComponent {
  @Input() cancellation: SaleCancellationDto | null = null;
  disclaimer(c: SaleCancellationDto): string {
    return (
      'Commercial cancellation record — not a GST credit note.' +
      (c.cashRefundAmount > 0
        ? " Cash refund is recorded from the operator's confirmation."
        : '')
    );
  }
}
