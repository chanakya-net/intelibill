import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { finalize } from 'rxjs';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { BillingPlansService, type PublishedPlan } from './billing-plans.service';

@Component({
  selector: 'app-billing-plans-page',
  imports: [RouterLink, TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './billing-plans-page.component.html',
  styleUrl: './billing-plans-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BillingPlansPageComponent {
  private readonly api = inject(BillingPlansService);
  private readonly destroy = inject(DestroyRef);
  protected readonly plans = signal<readonly PublishedPlan[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly unavailable = signal(false);

  constructor() {
    this.load(null);
  }

  protected load(after: string | null) {
    if (this.loading()) return;
    this.loading.set(true);
    this.unavailable.set(false);
    this.api.list(after).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.loading.set(false))).subscribe({
      next: (page) => {
        this.plans.update((current) => after ? [...current, ...page.items] : page.items);
        this.nextCursor.set(page.nextCursor);
      },
      error: () => this.unavailable.set(true),
    });
  }

  protected rupees(paise: number | null) {
    return paise === null ? null : paise / 100;
  }
}
