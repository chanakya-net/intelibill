import { CommonModule } from '@angular/common';
import { Component, Input, LOCALE_ID, inject } from '@angular/core';
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
          {{ cancellationTime(c) }} ({{ c.businessTimeZoneId }})
        </p>
        <p>{{ c.seller.legalName }} · {{ c.seller.address }}</p>
        <p>{{ c.buyer.name }}</p>
        <p>{{ 'sales.cancellation.reasonLabel' | transloco }}: {{ c.reason }}</p>
        @for (line of c.lines; track line.id) {
          <p>{{ line.description }} · {{ line.quantity | number: '1.0-3' }} {{ line.uqc }}</p>
          @if ((line.quarantinedQuantity ?? 0) > 0) { <p>{{ 'expiry.quarantine' | transloco }}: {{ line.quarantinedQuantity }}</p> }
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
  private readonly locale = inject(LOCALE_ID);
  @Input() cancellation: SaleCancellationDto | null = null;

  cancellationTime(c: SaleCancellationDto): string {
    return new Intl.DateTimeFormat(this.locale, {
      timeZone: c.businessTimeZoneId,
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      hour12: true,
    }).format(new Date(c.cancelledAt));
  }

  disclaimer(c: SaleCancellationDto): string {
    return (
      'Commercial cancellation record — not a GST credit note.' +
      (c.cashRefundAmount > 0
        ? " Cash refund is recorded from the operator's confirmation."
        : '')
    );
  }
}
