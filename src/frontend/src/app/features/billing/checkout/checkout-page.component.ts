import { KeyValuePipe, isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import type { PublishedPlan } from '../plans/billing-plans.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { CheckoutDrafts } from './checkout-drafts';
import { isUuid, type CheckoutShop, type Period } from './checkout.models';
import { CheckoutReview, DraftError, checkoutFailure, type Failure, type Invocation } from './checkout-review';
import { CheckoutService } from './checkout.service';
import { CheckoutPaymentComponent } from './checkout-payment.component';

@Component({
  selector: 'app-checkout-page',
  imports: [KeyValuePipe, ButtonDirective, RouterLink, TranslocoPipe, LocaleMoneyPipe, CheckoutPaymentComponent],
  providers: [CheckoutReview],
  templateUrl: './checkout-page.component.html',
  styleUrl: './checkout-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:storage)': 'onStorage($event)' },
})
export class CheckoutPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(CheckoutService);
  private readonly drafts = inject(CheckoutDrafts);
  private readonly review = inject(CheckoutReview);
  private readonly locale = inject(LocalizationService);
  private readonly scope = computed(() => {
    const account = this.params()?.get('accountId')?.toLowerCase();
    const owner = this.context.scope();
    return owner && isUuid(account) ? { ...owner, account } : null;
  });
  private readonly context = inject(ShopContextService);
  private readonly params = toSignal(this.route.paramMap);
  private readonly orderParam = toSignal(this.route.queryParamMap);
  private generation = 0;
  private readonly clock = signal(Date.now());

  private readonly checkoutPayment = viewChild(CheckoutPaymentComponent);
  protected readonly blocksNewQuote = computed(() =>
    !!this.quote()?.acceptance && (this.checkoutPayment()?.blocksNewQuote() ?? true));

  protected readonly quote = this.review.quote;
  protected readonly covered = this.review.covered;
  protected readonly candidates = signal<readonly CheckoutShop[]>([]);
  protected readonly plans = signal<readonly PublishedPlan[]>([]);
  protected readonly createDraft = this.review.createDraft;
  protected readonly acceptDraft = this.review.acceptDraft;
  protected readonly planId = signal('');
  protected readonly period = signal<Period>('Month');
  protected readonly term = signal('');
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly confirmed = signal(false);
  protected readonly busy = signal(false);
  protected readonly problem = this.review.problem;
  protected readonly openShops = computed(() => this.candidates().filter(shop => shop.termId === null));
  protected readonly renewals = computed(() => {
    const groups = new Map<string, string[]>();
    for (const { termId, name } of this.candidates()) {
      if (!termId) continue;
      const names = groups.get(termId) ?? [];
      names.push(name);
      groups.set(termId, names);
    }
    return groups;
  });
  protected readonly expired = computed(() => {
    const quote = this.quote();
    return !!quote && !quote.acceptance && (this.problem() === 'expired' || this.clock() >= Date.parse(quote.expiresAt));
  });

  constructor() {
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return;
    const timer = window.setInterval(() => this.clock.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => { this.generation++; window.clearInterval(timer); });
    effect(() => {
      this.scope();
      const order = this.orderParam()?.get('order');
      untracked(() => this.load(order));
    });
  }

  protected onStorage(event: StorageEvent) {
    const scope = this.scope();
    const order = this.quote()?.orderId;
    if (scope && !this.busy() && (event.key === this.drafts.key(scope) ||
      (order && event.key === this.drafts.key(scope, order)))) void this.load(this.orderParam()?.get('order'));
  }

  protected choose(event: Event, field: 'plan' | 'period' | 'term') {
    const value = (event.target as HTMLSelectElement).value;
    if (field === 'plan') this.planId.set(value);
    if (field === 'term') this.term.set(value);
    if (field === 'period') this.period.set(value === 'Year' ? 'Year' : 'Month');
  }

  protected toggle(shop: string, event: Event) {
    const next = new Set(this.selected());
    if ((event.target as HTMLInputElement).checked) next.add(shop);
    else next.delete(shop);
    this.selected.set(next);
  }

  protected async create() {
    const plan = this.plans().find(item => item.versionId === this.planId());
    const term = this.term() || null;
    if (!plan || this.busy() || this.createDraft() || (!term && !this.selected().size)) return;
    const ids = term ? [] : Array.from(this.selected()).sort();
    await this.perform(invocation => this.review.create(invocation, {
      planVersionId: plan.versionId, period: this.period(), shopIds: ids,
      extraShops: term ? 0 : Math.max(0, ids.length - plan.includedShops), expectedRevision: 1, termId: term,
    }), 'storage');
  }

  protected async accept() {
    const quote = this.quote();
    if (!quote || !this.canAccept()) return;
    await this.perform(invocation => this.review.accept(invocation, quote), 'storage');
  }

  protected async retry() {
    await this.perform(invocation => this.review.retry(invocation), 'unknown');
  }

  protected async startOver() {
    const quote = this.quote();
    if (!quote || this.busy() || (this.acceptDraft() && !this.acceptDraft()?.settled)) return;
    if (quote.acceptance && !this.checkoutPayment()?.canReviewNewQuote()) return;
    await this.perform(async invocation => {
      await this.drafts.startOver(invocation.scope, quote.orderId);
      if (!invocation.current()) return;
      if (this.orderParam()?.has('order'))
        await this.router.navigate([], { relativeTo: this.route, queryParams: { order: null }, queryParamsHandling: 'merge' });
      else void this.load(null);
    }, 'storage');
  }

  protected canAccept() {
    return !!this.covered() && this.confirmed() && !this.busy() && !this.expired() && !this.acceptDraft() &&
      !this.quote()?.acceptance && !this.problem();
  }

  protected reload() {
    void this.load(this.orderParam()?.get('order'));
  }

  protected instant(value: string) {
    return formatBillingInstant(value, this.locale.currentLanguage(), true);
  }

  private async load(order: string | null | undefined) {
    this.generation++;
    this.review.reset();
    this.confirmed.set(false);
    this.plans.set([]);
    this.candidates.set([]);
    this.selected.set(new Set());
    this.planId.set('');
    this.term.set('');
    this.busy.set(false);
    await this.perform(async invocation => {
      if (!await this.review.restore(invocation, order)) await this.loadOptions(invocation);
    }, 'unavailable');
  }

  private async loadOptions({ scope, current }: Invocation) {
    const [plans, shops] = await Promise.all([this.api.plans(current), this.api.shops(scope, null, current)]);
    if (!current()) return;
    this.plans.set(plans);
    this.candidates.set(shops);
  }

  private async perform(work: (invocation: Invocation) => Promise<void>, onError: Failure) {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    const generation = this.generation;
    const current = () => this.scope() === scope && this.generation === generation;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await work({ scope, current });
    } catch (error) {
      if (current()) this.problem.set(error instanceof DraftError ? 'storage' : checkoutFailure(error) ?? onError);
    } finally {
      if (current()) this.busy.set(false);
    }
  }
}
