import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { planKinds } from './catalogue-api.service';
import { formatInr } from './catalogue-money';
import type { CatalogueSnapshot } from './catalogue-publication.models';

@Component({
  selector: 'adm-catalogue-snapshot',
  templateUrl: './catalogue-snapshot.component.html',
  styleUrl: './catalogue-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogueSnapshotComponent {
  readonly snapshot = input.required<CatalogueSnapshot>();
  protected readonly plan = computed(() => {
    const snapshot = this.snapshot();
    return 'plan' in snapshot ? snapshot.plan : null;
  });
  protected readonly trial = computed(() => {
    const snapshot = this.snapshot();
    return 'plan' in snapshot ? snapshot.plan.trialPolicy : snapshot;
  });
  protected readonly accounting = computed(() => {
    const snapshot = this.snapshot();
    return 'plan' in snapshot ? snapshot.accountingTreatmentReference : null;
  });
  protected readonly graceExclusions = computed(() =>
    this.plan()?.graceExclusions.map((rule) => rule === 'ScheduledCancellation' ? 'Scheduled cancellation' : rule).join(', '));
  protected readonly reason = computed(() => {
    const snapshot = this.snapshot();
    return 'plan' in snapshot ? snapshot.negotiationReason : null;
  });
  protected readonly name = computed(() => this.plan()?.name ?? 'Trial policy');
  protected readonly version = computed(() => this.plan()?.versionNumber ?? this.trial().versionNumber);
  protected readonly inr = formatInr;
  protected readonly kinds = planKinds;
}
