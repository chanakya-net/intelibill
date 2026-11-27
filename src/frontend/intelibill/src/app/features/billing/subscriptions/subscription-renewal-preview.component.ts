import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { LocalizationService } from '../../../core/i18n/localization.service';
import type { Quote } from '../checkout/checkout.models';
import { formatBillingInstant } from '../status/subscription-status.models';

/**
 * Renewal review for a prior-paid-through quote. Every date comes from licensing's frozen quote: the previous
 * paid-through end, the new end on the original anchor and the grace days already included. Nothing is
 * calculated in the browser.
 */
@Component({
  selector: 'app-subscription-renewal-preview',
  imports: [TranslocoPipe],
  templateUrl: './subscription-renewal-preview.component.html',
  styles: [`
    :host { display: block; }
    section { display: grid; gap: 0.5rem; }
    h3, p { margin: 0; }
    dl { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 0.25rem 1rem; margin: 0; }
    dd { margin: 0; overflow-wrap: anywhere; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscriptionRenewalPreviewComponent {
  readonly quote = input.required<Quote>();
  private readonly locale = inject(LocalizationService);

  protected readonly renewal = computed(() => {
    const quote = this.quote();
    return quote.termId && quote.startRule === 'PriorPaidThrough' ? quote : null;
  });

  protected instant(value: string) {
    return formatBillingInstant(value, this.locale.currentLanguage(), true);
  }
}
