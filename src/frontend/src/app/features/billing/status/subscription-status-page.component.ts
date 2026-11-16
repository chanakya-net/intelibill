import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { Subscription } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { formatBillingInstant, type AccountTermStatus, type ShopSubscriptionStatus, type StatusFailure } from './subscription-status.models';
import { StatusReadError, SubscriptionStatusService } from './subscription-status.service';

type StatusView =
  | { readonly kind: 'loading' | 'noShop' }
  | { readonly kind: 'failed'; readonly failure: StatusFailure }
  | { readonly kind: 'shop'; readonly status: ShopSubscriptionStatus }
  | { readonly kind: 'account'; readonly evaluatedAt: string; readonly items: readonly AccountTermStatus[] };

/**
 * Read-only access status for the active shop or, for billing members, one account. Values are rendered
 * exactly as licensing evaluated them, in IST; nothing is computed here. A change of person or shop, or a
 * failed verification, clears the previous status, and a late answer from an earlier scope is discarded.
 */
@Component({
  selector: 'app-subscription-status-page',
  imports: [NgTemplateOutlet, RouterLink, TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './subscription-status-page.component.html',
  styleUrl: './subscription-status-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:focus)': 'revalidate()', '(document:visibilitychange)': 'revalidate()' },
})
export class SubscriptionStatusPageComponent {
  private readonly api = inject(SubscriptionStatusService);
  private readonly auth = inject(AuthService);
  private readonly localization = inject(LocalizationService);
  protected readonly accountId = inject(ActivatedRoute).snapshot.paramMap.get('accountId');
  private readonly scope = computed(() => {
    const session = this.auth.session();
    return session ? `${session.user.id}|${session.activeShopId ?? ''}` : '';
  });
  protected readonly view = signal<StatusView>({ kind: 'loading' });
  private request?: Subscription;
  private generation = 0;

  constructor() {
    effect(() => {
      this.scope();
      untracked(() => this.load());
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  protected load() {
    this.request?.unsubscribe();
    const generation = ++this.generation;
    this.view.set({ kind: 'loading' });
    const settle = (view: StatusView) => generation === this.generation && this.view.set(view);
    const fail = (error: unknown) => settle({ kind: 'failed', failure: error instanceof StatusReadError ? error.failure : 'unavailable' });
    if (this.accountId) {
      this.request = this.api.account(this.accountId).subscribe({
        next: result => settle({ kind: 'account', ...result }), error: fail,
      });
      return;
    }
    const shopId = this.auth.session()?.activeShopId;
    if (!shopId) {
      this.view.set({ kind: 'noShop' });
      return;
    }
    this.request = this.api.shop(shopId).subscribe({
      next: status => settle(status.shopId === shopId ? { kind: 'shop', status } : { kind: 'failed', failure: 'unavailable' }),
      error: fail,
    });
  }

  protected revalidate() {
    if (typeof document === 'undefined' || document.visibilityState === 'visible') this.load();
  }

  protected instant(value: string | null) {
    return formatBillingInstant(value, this.localization.currentLanguage());
  }

  protected rupees(paise: number | null) {
    return paise === null ? null : paise / 100;
  }
}
