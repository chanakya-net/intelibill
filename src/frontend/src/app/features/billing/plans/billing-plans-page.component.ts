import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { catchError, distinctUntilChanged, finalize, map, of, startWith, switchMap } from 'rxjs';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { BillingPlansService, type PublishedPlan, type PublishedPlanVersion } from './billing-plans.service';

type VersionSelection =
  | { status: 'catalogue' | 'loading' | 'missing' | 'unavailable' }
  | { status: 'ready'; version: PublishedPlanVersion };

const versionIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

@Component({
  selector: 'app-billing-plans-page',
  imports: [NgTemplateOutlet, RouterLink, TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './billing-plans-page.component.html',
  styleUrl: './billing-plans-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BillingPlansPageComponent {
  private readonly api = inject(BillingPlansService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy = inject(DestroyRef);
  protected readonly plans = signal<readonly PublishedPlan[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly unavailable = signal(false);

  protected readonly selection = toSignal(this.route.queryParamMap.pipe(
    map(params => params.get('version')),
    distinctUntilChanged(),
    switchMap(versionId => this.select(versionId)),
  ), { initialValue: { status: 'catalogue' } as VersionSelection });

  private select(versionId: string | null) {
    if (versionId === null) {
      this.load(null);
      return of<VersionSelection>({ status: 'catalogue' });
    }
    if (!versionIdPattern.test(versionId)) return of<VersionSelection>({ status: 'missing' });
    return this.api.version(versionId).pipe(
      map((version): VersionSelection => ({ status: 'ready', version })),
      startWith<VersionSelection>({ status: 'loading' }),
      catchError((error: HttpErrorResponse) =>
        of<VersionSelection>({ status: error.status === 404 ? 'missing' : 'unavailable' })),
    );
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
