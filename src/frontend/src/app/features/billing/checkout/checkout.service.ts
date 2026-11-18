import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { BillingPlansService, type BillingPlansPage, type PublishedPlan } from '../plans/billing-plans.service';
import {
  shopSetHash, type AcceptQuote, type CheckoutScope, type CheckoutShop, type CheckoutShopPage, type CreateQuote, type Quote,
} from './checkout.models';

@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly http = inject(HttpClient);
  private readonly catalogue = inject(BillingPlansService);

  create(scope: CheckoutScope, command: CreateQuote) {
    return firstValueFrom(this.http.post<Quote>(this.path(scope), command, this.options(scope)));
  }

  accept(scope: CheckoutScope, order: string, command: AcceptQuote) {
    return firstValueFrom(this.http.post<Quote>(this.path(scope, order) + '/accept', command, this.options(scope)));
  }

  read(scope: CheckoutScope, order: string) {
    return firstValueFrom(this.http.get<Quote>(this.path(scope, order), this.options(scope)));
  }

  recover(scope: CheckoutScope, action: 'create' | 'accept', key: string) {
    return firstValueFrom(this.http.get<Quote>(
      this.path(scope) + '/operations/' + action + '/' + encodeURIComponent(key), this.options(scope)));
  }

  async plans(current: () => boolean): Promise<PublishedPlan[]> {
    const plans: PublishedPlan[] = [];
    let after: string | null = null;
    do {
      const page: BillingPlansPage = await firstValueFrom(this.catalogue.list(after));
      this.checkProgress(current, page.nextCursor, after);
      plans.push(...page.items);
      after = page.nextCursor;
    } while (after);
    return plans;
  }

  async shops(scope: CheckoutScope, quote: Quote | null, current: () => boolean): Promise<CheckoutShop[]> {
    const url = quote ? this.path(scope, quote.orderId) + '/shops' : this.path(scope) + '/eligible-shops';
    const shops: CheckoutShop[] = [];
    let after: string | null = null;
    do {
      const page = await this.shopPage(url, scope, after);
      this.checkProgress(current, page.nextCursor, after);
      if (page.accountId !== scope.account || page.orderId !== (quote?.orderId ?? null))
        throw new Error('Invalid checkout shop scope');
      shops.push(...page.items);
      if (quote && shops.length > quote.shopCount) throw new Error('Invalid checkout shop count');
      after = page.nextCursor;
    } while (after);

    if (quote && (shops.length !== quote.shopCount || shops.some(shop => shop.termId !== quote.termId) ||
        await shopSetHash(shops.map(shop => shop.shopId)) !== quote.shopHash))
      throw new Error('Invalid checkout shop set');
    return shops;
  }

  private shopPage(url: string, scope: CheckoutScope, after: string | null): Promise<CheckoutShopPage> {
    return firstValueFrom(this.http.get<CheckoutShopPage>(url,
      { ...this.options(scope), params: after ? { after } : {} }));
  }

  private checkProgress(current: () => boolean, next: string | null, prior: string | null) {
    if (!current() || (next && prior && next <= prior)) throw new Error('Checkout read did not advance');
  }

  private path(scope: CheckoutScope, order?: string): string {
    const base = API_BASE_URL + '/billing/accounts/' + encodeURIComponent(scope.account) + '/orders';
    return order ? base + '/' + encodeURIComponent(order) : base;
  }

  private options(scope: CheckoutScope) {
    return { context: scopedRequestContext(scope) };
  }
}
