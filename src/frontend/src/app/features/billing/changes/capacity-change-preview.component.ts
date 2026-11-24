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
import { CapacityChangeQuoteDrafts } from './capacity-change-quote-drafts';
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
        .filter(plan => plan.membersPerShop >= (this.term().memberAllowance ?? 0));
      if (generation !== this.generation) return;
      this.planId.set(plans[0]?.versionId ?? null);
      this.capacity.set(Math.max(this.term().purchasedCapacity + 1, plans[0]?.includedShops ?? 0));
      this.view.set({ kind: 'form', plans });
    } catch (error) {
      if (generation === this.generation) this.fail(error);
    }
  }

  protected async preview() {
    const scope = this.scope();
    const term = this.term();
    const plan = this.planId();
    if (!scope || !plan) return;
    const generation = ++this.generation;
    const command: CreateCapacityChangeQuote = {
      operationKey: crypto.randomUUID(), targetPlanVersionId: plan, targetCapacity: this.capacity(),
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
    const draft = this.drafts.read(scope, term.termId);
    if (draft) await this.drafts.discard(scope, term.termId, draft.command.operationKey);
    if (view.kind === 'quote' || view.kind === 'failed') await this.open();
  }

  private async send(scope: CheckoutScope, term: SubscriptionTermStatus, command: CreateCapacityChangeQuote,
    current: () => boolean) {
    this.view.set({ kind: 'loading' });
    const quote = await this.api.create(scope, term.termId, command);
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
