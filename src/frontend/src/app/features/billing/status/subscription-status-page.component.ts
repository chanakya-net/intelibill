import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { map, Subscription } from 'rxjs';
import { CapacityChangePreviewComponent } from '../changes/capacity-change-preview.component';
import { AccountClosureComponent } from '../closure/account-closure.component';
import { ClosureConfirmationComponent } from '../closure/closure-confirmation.component';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import {
  formatBillingInstant, isShopTradingAuthority, type AccountTermStatus, type ShopSubscriptionStatus, type ShopTradingAuthority, type StatusFailure, type SubscriptionTermStatus,
  type TradingAuthorityFailure,
} from './subscription-status.models';
import { SubscriptionCancellationComponent } from '../subscriptions/subscription-cancellation.component';
import { SubscriptionCancellationWithdrawalComponent } from '../subscriptions/subscription-cancellation-withdrawal.component';
import { EntitlementSynchronizationComponent } from './entitlement-synchronization.component';
import { StatusReadError, SubscriptionStatusService, TradingAuthorityReadError } from './subscription-status.service';

type StatusView =
  | { readonly kind: 'loading' | 'noShop' }
  | { readonly kind: 'failed'; readonly failure: StatusFailure }
  | { readonly kind: 'shop'; readonly status: ShopSubscriptionStatus }
  | { readonly kind: 'account'; readonly evaluatedAt: string; readonly items: readonly AccountTermStatus[] };

type TradingAuthorityView =
  | { readonly kind: 'loading' }
  | { readonly kind: 'verified'; readonly authority: ShopTradingAuthority }
  | { readonly kind: 'failed'; readonly failure: TradingAuthorityFailure };

/**
 * Read-only access status for the active shop or, for billing members, one account. Values are rendered
 * exactly as licensing evaluated them, in IST; nothing is computed here. A change of person or shop, or a
 * failed verification, clears the previous status, and a late answer from an earlier scope is discarded.
 */
@Component({
  selector: 'app-subscription-status-page',
  imports: [NgTemplateOutlet, RouterLink, TranslocoPipe, LocaleMoneyPipe, EntitlementSynchronizationComponent, SubscriptionCancellationComponent,
    SubscriptionCancellationWithdrawalComponent, ClosureConfirmationComponent, AccountClosureComponent, CapacityChangePreviewComponent],
  templateUrl: './subscription-status-page.component.html',
  styleUrl: './subscription-status-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:focus)': 'revalidate()', '(document:visibilitychange)': 'revalidate()' },
})
export class SubscriptionStatusPageComponent {
  private readonly api = inject(SubscriptionStatusService);
  private readonly auth = inject(AuthService);
  private readonly localization = inject(LocalizationService);
  private readonly route = inject(ActivatedRoute);
  protected readonly accountId = toSignal(this.route.paramMap.pipe(map(params => params.get('accountId')?.toLowerCase() ?? null)),
    { initialValue: this.route.snapshot.paramMap.get('accountId')?.toLowerCase() ?? null });
  protected readonly isShopOwner = computed(() => {
    const session = this.auth.session();
    return !!session?.activeShopId && session.shops?.some(shop => shop.shopId === session.activeShopId && shop.role === 'Owner');
  });
  private readonly scope = computed(() => {
    const session = this.auth.session();
    return session ? `${session.user.id}|${session.activeShopId ?? ''}` : '';
  });
  protected readonly view = signal<StatusView>({ kind: 'loading' });
  protected readonly trading = signal<TradingAuthorityView | null>(null);
  private request?: Subscription;
  private authorityRequest?: Subscription;
  private generation = 0;

  constructor() {
    effect(() => {
      this.scope();
      this.accountId();
      untracked(() => this.load());
    });
    inject(DestroyRef).onDestroy(() => {
      this.request?.unsubscribe();
      this.authorityRequest?.unsubscribe();
    });
  }

  protected load() {
    this.request?.unsubscribe();
    this.authorityRequest?.unsubscribe();
    this.trading.set(null);
    const generation = ++this.generation;
    this.view.set({ kind: 'loading' });
    const settle = (view: StatusView) => generation === this.generation && this.view.set(view);
    const fail = (error: unknown) => settle({ kind: 'failed', failure: error instanceof StatusReadError ? error.failure : 'unavailable' });
    const account = this.accountId();
    if (account) {
      this.request = this.api.account(account).subscribe({
        next: result => settle({ kind: 'account', ...result }), error: fail,
      });
      return;
    }
    const shopId = this.auth.session()?.activeShopId;
    if (!shopId) {
      this.view.set({ kind: 'noShop' });
      return;
    }
    this.loadTradingAuthority(shopId, generation);
    this.request = this.api.shop(shopId).subscribe({
      next: status => settle(status.shopId === shopId ? { kind: 'shop', status } : { kind: 'failed', failure: 'unavailable' }),
      error: fail,
    });
  }

  private loadTradingAuthority(shopId: string, generation: number) {
    this.trading.set({ kind: 'loading' });
    const settle = (view: TradingAuthorityView) => generation === this.generation && this.trading.set(view);
    this.authorityRequest = this.api.tradingAuthority(shopId).subscribe({
      next: authority => settle(isShopTradingAuthority(authority, shopId)
        ? { kind: 'verified', authority } : { kind: 'failed', failure: 'unavailable' }),
      error: (error: unknown) => settle({ kind: 'failed',
        failure: error instanceof TradingAuthorityReadError ? error.failure : 'unavailable' }),
    });
  }

  protected revalidate() {
    if ((this.request && !this.request.closed) || (this.authorityRequest && !this.authorityRequest.closed)) return;
    if (document.visibilityState === 'visible') this.load();
  }

  protected instant(value: string | null, exact = false) {
    return formatBillingInstant(value, this.localization.currentLanguage(), exact);
  }

  /** Trial and courtesy deadlines are exact instants (not calendar boundaries), shown to the recorded second. */
  protected exact(term: SubscriptionTermStatus) {
    return term.basis === 'Trial' || term.basis === 'Complimentary';
  }

  /** A courtesy grant has an access end, never a paid-through date. */
  protected endLabel(term: SubscriptionTermStatus) {
    return term.basis === 'Trial' ? 'billing.trial.deadline'
      : term.basis === 'Complimentary' ? 'billing.status.courtesyEnds' : 'billing.status.paidThrough';
  }

  protected rupees(paise: number | null) {
    return paise === null ? null : paise / 100;
  }
}
