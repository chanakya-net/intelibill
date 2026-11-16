import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import type { EntitlementSynchronization } from '@intelibill/api';
import { TranslocoPipe } from '@ngneat/transloco';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from './subscription-status.models';

@Component({
  selector: 'app-entitlement-synchronization',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './entitlement-synchronization.component.html',
  styles: [`
    :host { display: block; }
    .facts { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.4fr); gap: 0.5rem; margin: 0; }
    dd { margin: 0; overflow-wrap: anywhere; }
  `],
})
export class EntitlementSynchronizationComponent {
  readonly synchronization = input<EntitlementSynchronization | null>(null);
  private readonly localization = inject(LocalizationService);

  protected instant(value: string | null) {
    return formatBillingInstant(value, this.localization.currentLanguage(), true);
  }
}
