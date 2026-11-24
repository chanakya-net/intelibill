import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { shopSetHash, type CheckoutScope } from '../checkout/checkout.models';

/** Mirror of capacity-change-quote/1. The browser renders these frozen facts and never recalculates a charge. */
export type CapacityChangeOutcome =
  | 'Payable' | 'AtRenewalOrCourtesy' | 'RenewalRequired' | 'NewTermRequired' | 'TrialConversion' | 'PaymentUnavailable';

export interface CreateCapacityChangeQuote {
  readonly operationKey: string;
  readonly targetPlanVersionId: string;
  readonly targetCapacity: number;
  readonly observedCapacity: number;
  readonly observedPaidThrough: string;
}

export interface CapacityChangeQuote {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly quoteId: string;
  readonly termId: string;
  readonly outcome: CapacityChangeOutcome;
  readonly reason: string | null;
  readonly actions: readonly string[];
  readonly targetPlanVersionId: string;
  readonly targetPlanName: string;
  readonly period: 'Month' | 'Year';
  readonly priorCapacity: number;
  readonly targetCapacity: number;
  readonly priorMemberAllowance: number;
  readonly targetMemberAllowance: number;
  readonly shopCount: number;
  readonly shopHash: string;
  readonly purchasedGrossPaise: number | null;
  readonly targetGrossPaise: number;
  readonly differencePaise: number | null;
  readonly amountPaise: number | null;
  readonly currency: 'INR';
  readonly intervalStartsAt: string;
  readonly paidThrough: string;
  readonly evaluatedAt: string;
  readonly totalSeconds: number | null;
  readonly remainingSeconds: number | null;
  readonly anchorLocal: string | null;
  readonly suspensionDisclosed: boolean;
  readonly createdAt: string;
  readonly expiresAt: string;
  readonly validUntil: string;
  readonly developmentFixture: boolean;
  readonly snapshotHash: string;
}

export interface CapacityChangeShop { readonly shopId: string; readonly name: string }
interface CapacityChangeShopPage {
  readonly accountId: string;
  readonly quoteId: string;
  readonly items: readonly CapacityChangeShop[];
  readonly nextCursor: string | null;
}

const outcomes: readonly CapacityChangeOutcome[] =
  ['Payable', 'AtRenewalOrCourtesy', 'RenewalRequired', 'NewTermRequired', 'TrialConversion', 'PaymentUnavailable'];

/** Structural check of a relayed quote against the exact request that produced it. */
export function matchesRequest(quote: CapacityChangeQuote, scope: CheckoutScope, term: string,
  command: CreateCapacityChangeQuote): boolean {
  return quote.schemaVersion === 1 && quote.accountId === scope.account && quote.termId === term &&
    outcomes.includes(quote.outcome) && quote.targetPlanVersionId === command.targetPlanVersionId &&
    quote.targetCapacity === command.targetCapacity && quote.priorCapacity === command.observedCapacity &&
    (quote.outcome === 'Payable') === (typeof quote.amountPaise === 'number' && quote.amountPaise > 0);
}

@Injectable({ providedIn: 'root' })
export class CapacityChangeQuoteService {
  private readonly http = inject(HttpClient);

  create(scope: CheckoutScope, term: string, command: CreateCapacityChangeQuote): Promise<CapacityChangeQuote> {
    return firstValueFrom(this.http.post<CapacityChangeQuote>(this.path(scope, term), command,
      { context: scopedRequestContext(scope) }));
  }

  /** Every frozen shop of the quote, page by page; the set must match the quote's count and hash. */
  async shops(scope: CheckoutScope, quote: CapacityChangeQuote, current: () => boolean): Promise<CapacityChangeShop[]> {
    const shops: CapacityChangeShop[] = [];
    let after: string | null = null;
    do {
      const page: CapacityChangeShopPage = await firstValueFrom(this.http.get<CapacityChangeShopPage>(
        this.path(scope, quote.termId) + '/' + encodeURIComponent(quote.quoteId) + '/shops',
        { context: scopedRequestContext(scope), params: after ? { after } : {} }));
      if (!current() || page.quoteId !== quote.quoteId || (page.nextCursor && after && page.nextCursor <= after))
        throw new Error('Capacity quote shop read did not advance');
      shops.push(...page.items);
      after = page.nextCursor;
    } while (after && shops.length <= quote.shopCount);
    if (shops.length !== quote.shopCount || await shopSetHash(shops.map(shop => shop.shopId)) !== quote.shopHash)
      throw new Error('Invalid capacity quote shop set');
    return shops;
  }

  private path(scope: CheckoutScope, term: string) {
    return API_BASE_URL + '/billing/accounts/' + encodeURIComponent(scope.account) + '/subscriptions/' +
      encodeURIComponent(term) + '/capacity-change-quotes';
  }
}
