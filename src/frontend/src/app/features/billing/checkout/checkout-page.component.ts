import { KeyValuePipe, isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, PLATFORM_ID, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import type { PublishedPlan } from '../plans/billing-plans.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { CheckoutDrafts, type AcceptDraft, type CreateDraft } from './checkout-drafts';
import { isUuid, shopSetHash, type CheckoutScope, type CheckoutShop, type Period, type Quote } from './checkout.models';
import { CheckoutService } from './checkout.service';

type Failure = 'unavailable' | 'storage' | 'unknown' | 'invalid' | 'changed' | 'expired' | 'denied';
type Invocation = { readonly scope: CheckoutScope; readonly current: () => boolean };

@Component({
  selector: 'app-checkout-page',
  imports: [KeyValuePipe, ButtonDirective, RouterLink, TranslocoPipe, LocaleMoneyPipe],
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

  protected readonly quote = signal<Quote | null>(null);
  protected readonly covered = signal<readonly CheckoutShop[] | null>(null);
  protected readonly candidates = signal<readonly CheckoutShop[]>([]);
  protected readonly plans = signal<readonly PublishedPlan[]>([]);
  protected readonly createDraft = signal<CreateDraft | null>(null);
  protected readonly acceptDraft = signal<AcceptDraft | null>(null);
  protected readonly planId = signal('');
  protected readonly period = signal<Period>('Month');
  protected readonly term = signal('');
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly confirmed = signal(false);
  protected readonly busy = signal(false);
  protected readonly problem = signal<Failure | null>(null);
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
    await this.perform(async invocation => {
      const key = crypto.randomUUID();
      const ids = term ? [] : Array.from(this.selected()).sort();
      const draft = await this.drafts.prepareCreate(invocation.scope, {
        operationKey: key, planVersionId: plan.versionId, period: this.period(), shopIds: ids,
        extraShops: term ? 0 : Math.max(0, ids.length - plan.includedShops), expectedRevision: 1, termId: term,
      });
      if (!invocation.current()) return;
      this.createDraft.set(draft);
      if (draft.command.operationKey === key) await this.sendCreate(invocation, draft);
      else if (draft.order) await this.restoreOrder(invocation, draft.order);
      else this.problem.set('unknown');
    }, 'storage');
  }

  protected async accept() {
    const quote = this.quote();
    if (!quote || !this.canAccept()) return;
    await this.perform(async invocation => {
      const draft = await this.drafts.prepareAccept(invocation.scope, quote.orderId, {
        operationKey: crypto.randomUUID(), expectedRevision: quote.revision, snapshotHash: quote.snapshotHash,
        customerTermsVersion: quote.customerTermsVersion, refundPolicyVersion: quote.refundPolicyVersion, confirmed: true,
      });
      if (!invocation.current()) return;
      this.acceptDraft.set(draft);
      await this.sendAccept(invocation, quote, draft);
    }, 'storage');
  }

  protected async retry() {
    const create = this.createDraft();
    const accept = this.acceptDraft();
    const quote = this.quote();
    await this.perform(async invocation => {
      if (accept && quote) await this.sendAccept(invocation, quote, accept);
      else if (create && !create.order) await this.sendCreate(invocation, create);
    }, 'unknown');
  }

  protected async startOver() {
    const quote = this.quote();
    if (!quote || this.busy() || (this.acceptDraft() && !this.acceptDraft()?.settled)) return;
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
    this.quote.set(null);
    this.covered.set(null);
    this.acceptDraft.set(null);
    this.createDraft.set(null);
    this.confirmed.set(false);
    this.plans.set([]);
    this.candidates.set([]);
    this.selected.set(new Set());
    this.planId.set('');
    this.term.set('');
    this.busy.set(false);
    await this.perform(async invocation => {
      const draft = this.readLocal(() => this.drafts.readCreate(invocation.scope));
      if (!order) this.createDraft.set(draft);
      const retained = order ?? draft?.order;
      if (retained && isUuid(retained)) await this.restoreOrder(invocation, retained);
      else if (order) this.problem.set('invalid');
      else if (draft) await this.recoverCreate(invocation, draft);
      else await this.loadOptions(invocation);
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

  private async recoverCreate(invocation: Invocation, draft: CreateDraft) {
    try {
      await this.saveCreated(invocation, draft,
        await this.api.recover(invocation.scope, 'create', draft.command.operationKey));
    } catch (error) {
      if (error instanceof DraftError) throw error;
      if (invocation.current()) this.problem.set(checkoutFailure(error) ?? 'unknown');
    }
  }

  private async sendCreate(invocation: Invocation, draft: CreateDraft) {
    const { scope, current } = invocation;
    try {
      await this.saveCreated(invocation, draft, await this.api.create(scope, draft.command));
    } catch (error) {
      if (error instanceof DraftError) throw error;
      const failure = checkoutFailure(error);
      if (failure === 'invalid' || failure === 'changed') {
        await this.writeLocal(() => this.drafts.finishCreate(scope, draft.command.operationKey, null));
        if (current()) this.createDraft.set(null);
      }
      if (current()) this.problem.set(failure ?? 'unknown');
    }
  }

  private async saveCreated(invocation: Invocation, draft: CreateDraft, quote: Quote) {
    const { scope, current } = invocation;
    const command = draft.command;
    if (quote.accountId !== scope.account || quote.planVersionId !== command.planVersionId ||
      quote.period !== command.period || quote.termId !== command.termId ||
      (!quote.termId && (quote.shopCount !== command.shopIds.length ||
        await shopSetHash(command.shopIds) !== quote.shopHash))) throw new Error('Quote does not match selection');
    await this.writeLocal(() => this.drafts.finishCreate(scope, command.operationKey, quote.orderId));
    if (current()) {
      this.createDraft.set({ ...draft, order: quote.orderId });
      await this.show(invocation, quote);
    }
  }

  private async restoreOrder(invocation: Invocation, order: string) {
    const { scope, current } = invocation;
    const draft = this.readLocal(() => this.drafts.readAccept(scope, order));
    this.acceptDraft.set(draft);
    if (draft && !draft.settled) {
      try {
        const recovered = await this.api.recover(scope, 'accept', draft.command.operationKey);
        await this.saveAccepted(invocation, order, draft, recovered);
        return;
      } catch (error) {
        if (error instanceof DraftError) throw error;
        if (current()) this.problem.set(checkoutFailure(error) ?? 'unknown');
      }
    }
    if (!current()) return;
    const quote = await this.api.read(scope, order);
    if (draft && !draft.settled && quote.acceptance) {
      if (quote.acceptance.operationKey === draft.command.operationKey) {
        await this.saveAccepted(invocation, order, draft, quote);
        return;
      }
      await this.writeLocal(() => this.drafts.finishAccept(scope, order, draft.command.operationKey, false));
      if (current()) { this.acceptDraft.set(null); this.problem.set(null); }
    }
    await this.show(invocation, quote);
  }

  private async sendAccept(invocation: Invocation, quote: Quote, draft: AcceptDraft) {
    const { scope, current } = invocation;
    try {
      await this.saveAccepted(invocation, quote.orderId, draft, await this.api.accept(scope, quote.orderId, draft.command));
    } catch (error) {
      if (error instanceof DraftError) throw error;
      if (error instanceof HttpErrorResponse && error.status === 409 &&
        error.error?.code === 'Billing.CheckoutAlreadyAccepted') {
        if (current()) await this.restoreOrder(invocation, quote.orderId);
        return;
      }
      const failure = checkoutFailure(error);
      if (failure === 'expired' || failure === 'changed' || failure === 'invalid') {
        await this.writeLocal(() => this.drafts.finishAccept(scope, quote.orderId, draft.command.operationKey, false));
        if (current()) this.acceptDraft.set(null);
      }
      if (current()) this.problem.set(failure ?? 'unknown');
    }
  }

  private async saveAccepted(invocation: Invocation, order: string, draft: AcceptDraft, quote: Quote) {
    const { scope, current } = invocation;
    const { operationKey, snapshotHash } = draft.command;
    if (quote.accountId !== scope.account || quote.orderId !== order || quote.acceptance?.operationKey !== operationKey ||
      quote.snapshotHash !== snapshotHash || quote.acceptance.snapshotHash !== snapshotHash)
      throw new Error('Acceptance does not match review');
    await this.writeLocal(() => this.drafts.finishAccept(scope, order, operationKey, true));
    if (!current()) return;
    this.acceptDraft.set({ ...draft, settled: true });
    this.problem.set(null);
    await this.show(invocation, quote);
  }

  private async show({ scope, current }: Invocation, quote: Quote) {
    if (!current()) return;
    if (quote.accountId !== scope.account || quote.schemaVersion !== 1) throw new Error('Invalid quote');
    this.quote.set(quote);
    this.covered.set(null);
    try {
      const shops = await this.api.shops(scope, quote, current);
      if (current()) this.covered.set(shops);
    } catch {
      if (current()) this.problem.set('unavailable');
    }
  }

  private readLocal<T>(operation: () => T): T {
    try { return operation(); } catch { throw new DraftError(); }
  }

  private async writeLocal<T>(operation: () => Promise<T>): Promise<T> {
    try { return await operation(); } catch { throw new DraftError(); }
  }
}

class DraftError extends Error {}

function checkoutFailure(error: unknown): Failure | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  if (error.status === 401 || error.status === 403) return 'denied';
  switch (error.error?.code) {
    case 'Billing.CheckoutExpired': return 'expired';
    case 'Billing.CheckoutSourceChanged': return 'changed';
    case 'Billing.CheckoutInvalid': return 'invalid';
    default: return null;
  }
}
