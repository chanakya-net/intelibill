import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import type { CheckoutScope } from '../checkout/checkout.models';
import { CheckoutService } from '../checkout/checkout.service';
import type { PublishedPlan } from '../plans/billing-plans.service';
import { BillingAccountService } from '../services/billing-account.service';
import { formatBillingInstant, type SubscriptionTermStatus } from '../status/subscription-status.models';
import { CapacityChangeQuoteDrafts, InvalidCapacityChangeDraft } from './capacity-change-quote-drafts';
import {
  CapacityChangeQuoteService, matchesRequest, type CapacityChangeQuote, type CapacityChangeShop, type CreateCapacityChangeQuote,
} from './capacity-change-quote.service';

type PreviewView =
  | { readonly kind: 'hidden' | 'closed' | 'loading' }
  | { readonly kind: 'form'; readonly plans: readonly PublishedPlan[] }
  | { readonly kind: 'quote'; readonly quote: CapacityChangeQuote; readonly shops: readonly CapacityChangeShop[] }
  | { readonly kind: 'failed'; readonly code: string };

const knownCodes: Readonly<Record<string, string>> = {
  'Billing.CapacityChangeInvalid': 'invalid', 'Billing.CapacityChangeSourceChanged': 'changed',
  'Billing.CapacityChangeNeedsReview': 'review', 'Billing.CapacityChangeNotFound': 'notFound',
  'Billing.OperationPayloadMismatch': 'mismatch',
};

/**
 * Billing-owner preview of a prorated capacity increase for one subscription term. The request is saved before it is
 * sent and resent unchanged after a reload, so the one frozen quote is recovered. Every amount, date and shop comes
 * from licensing; an answer from an earlier session, account or term is discarded.
 */
@Component({
  selector: 'app-capacity-change-preview',
  imports: [FormsModule, RouterLink, TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './capacity-change-preview.component.html',
  styles: [`
    :host { display: block; }
    section { display: grid; gap: 0.5rem; }
    h3, p { margin: 0; }
    dl { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 0.25rem 1rem; margin: 0; }
    dd { margin: 0; overflow-wrap: anywhere; }
    form { display: grid; gap: 0.5rem; max-width: 32rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CapacityChangePreviewComponent {
  readonly account = input.required<string>();
  readonly term = input.required<SubscriptionTermStatus>();
  private readonly context = inject(ShopContextService);
  private readonly accounts = inject(BillingAccountService);
  private readonly checkout = inject(CheckoutService);
  private readonly api = inject(CapacityChangeQuoteService);
  private readonly drafts = inject(CapacityChangeQuoteDrafts);
  private readonly locale = inject(LocalizationService);
  protected readonly view = signal<PreviewView>({ kind: 'hidden' });
  protected readonly planId = signal<string | null>(null);
  protected readonly capacity = signal(0);
  protected readonly selectedPlan = computed(() => {
    const view = this.view();
    return view.kind === 'form' ? view.plans.find(plan => plan.versionId === this.planId()) : undefined;
  });
  private generation = 0;

  private readonly scope = computed<CheckoutScope | null>(() => {
    const owner = this.context.scope();
    return owner ? { userId: owner.userId, shopId: owner.shopId, account: this.account() } : null;
  });

  constructor() {
    effect(() => {
      const scope = this.scope();
      const term = this.term();
      untracked(() => void this.start(scope, term));
    });
  }

  private async start(scope: CheckoutScope | null, term: SubscriptionTermStatus) {
    const generation = ++this.generation;
    this.view.set({ kind: 'hidden' });
    if (!scope || !['Paid', 'Trial'].includes(term.basis ?? '')) return;
    const current = () => generation === this.generation;
    try {
      const listed = await firstValueFrom(this.accounts.list());
      if (!current() || listed.find(item => item.accountId === scope.account)?.role !== 'Owner') return;
      const draft = this.drafts.read(scope, term.termId);
      if (draft) await this.send(scope, term, draft.command, current);
      else this.view.set({ kind: 'closed' });
    } catch (error) {
      if (current()) this.fail(error);
    }
  }

  protected async open() {
    const scope = this.scope();
    const generation = ++this.generation;
    if (!scope) return;
    this.view.set({ kind: 'loading' });
    try {
      const plans = (await this.checkout.plans(() => generation === this.generation))
        .filter(plan => plan.membersPerShop >= (this.term().memberAllowance ?? 0) &&
          (plan.extraShopsOffered || plan.includedShops >= this.minimumCapacity(plan)));
      if (generation !== this.generation) return;
      this.view.set({ kind: 'form', plans });
      this.capacity.set(0);
      this.selectPlan(plans[0]?.versionId ?? null);
    } catch (error) {
      if (generation === this.generation) this.fail(error);
    }
  }

  protected minimumCapacity(plan: PublishedPlan | undefined): number {
    const term = this.term();
    const allowsSameSlots = term.basis === 'Trial' || (plan?.membersPerShop ?? 0) > (term.memberAllowance ?? 0);
    return Math.max(term.purchasedCapacity + (allowsSameSlots ? 0 : 1), plan?.includedShops ?? 0);
  }

  protected selectPlan(id: string | null) {
    this.planId.set(id);
    const plan = this.selectedPlan();
    if (!plan) return;
    this.capacity.set(plan.extraShopsOffered ? Math.max(this.capacity(), this.minimumCapacity(plan)) : plan.includedShops);
  }

  protected async preview() {
    const scope = this.scope();
    const term = this.term();
    const plan = this.selectedPlan();
    const capacity = this.capacity();
    if (!scope || !plan || !Number.isSafeInteger(capacity) || capacity < this.minimumCapacity(plan)) return;
    const generation = ++this.generation;
    const command: CreateCapacityChangeQuote = {
      operationKey: crypto.randomUUID(), targetPlanVersionId: plan.versionId, targetCapacity: capacity,
      observedCapacity: term.purchasedCapacity, observedPaidThrough: term.paidThrough,
    };
    this.view.set({ kind: 'loading' });
    try {
      const saved = await this.drafts.prepare(scope, term.termId, command);
      await this.send(scope, term, saved.command, () => generation === this.generation);
    } catch (error) {
      if (generation === this.generation) this.fail(error);
    }
  }

  /** Expired or refused previews end only by this explicit choice; the next request gets a fresh key. */
  protected async startOver() {
    const scope = this.scope();
    const view = this.view();
    const term = this.term();
    if (!scope) return;
    const generation = ++this.generation;
    try {
      await this.drafts.discard(scope, term.termId, this.savedKey(scope, term.termId));
      if (generation === this.generation && (view.kind === 'quote' || view.kind === 'failed')) await this.open();
    } catch (error) {
      if (generation === this.generation) this.fail(error);
    }
  }

  private savedKey(scope: CheckoutScope, term: string): string | undefined {
    try {
      return this.drafts.read(scope, term)?.command.operationKey;
    } catch (error) {
      if (!(error instanceof InvalidCapacityChangeDraft)) throw error;
      return undefined;
    }
  }

  private async send(scope: CheckoutScope, term: SubscriptionTermStatus, command: CreateCapacityChangeQuote,
    current: () => boolean) {
    this.view.set({ kind: 'loading' });
    const quote = await this.api.create(scope, term.termId, command).catch(async error => {
      if (error instanceof HttpErrorResponse && [400, 403, 404, 409].includes(error.status))
        await this.drafts.discard(scope, term.termId, command.operationKey);
      throw error;
    });
    if (!current()) return;
    if (!matchesRequest(quote, scope, term.termId, command)) throw new Error('Capacity quote does not match its request');
    const shops = await this.api.shops(scope, quote, current);
    if (current()) this.view.set({ kind: 'quote', quote, shops });
  }

  private fail(error: unknown) {
    const http = error instanceof HttpErrorResponse ? error : null;
    const code = (typeof http?.error?.code === 'string' ? knownCodes[http.error.code] : undefined)
      ?? (http?.status === 403 ? 'denied' : 'unavailable');
    if (code === 'denied') this.view.set({ kind: 'hidden' });
    else this.view.set({ kind: 'failed', code });
  }

  protected expired(quote: CapacityChangeQuote) {
    return Date.now() >= Date.parse(quote.validUntil);
  }

  protected instant(value: string | null) {
    return formatBillingInstant(value, this.locale.currentLanguage(), true);
  }

  protected rupees(paise: number | null) {
    return paise === null ? null : paise / 100;
  }
}
