import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, type Observable } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { CheckoutScope } from './checkout.models';
import { isCheckoutPayment, type CheckoutPayment } from './checkout-payment.models';

@Injectable({ providedIn: 'root' })
export class CheckoutPaymentService {
  private readonly http = inject(HttpClient);

  read(scope: CheckoutScope, order: string) {
    return this.checked(scope, order, this.http.get<unknown>(this.path(scope, order), this.options(scope)));
  }

  start(scope: CheckoutScope, order: string, command: { operationKey: string; expectedRevision: number; snapshotHash: string }) {
    return this.checked(scope, order, this.http.post<unknown>(this.path(scope, order) + '/attempts', command, this.options(scope)));
  }

  /** The launcher's report is only a hint; the server verifies captured funds itself. */
  verify(scope: CheckoutScope, order: string, attempt: string, launcherOutcome: 'Success' | 'Cancelled' | null) {
    return this.checked(scope, order, this.http.post<unknown>(this.attempt(scope, order, attempt) + '/verify',
      { launcherOutcome }, this.options(scope)));
  }

  /** Development simulator only; the server answers 404 outside a local simulator runtime. */
  simulateCapture(scope: CheckoutScope, order: string, attempt: string) {
    return this.checked(scope, order, this.http.post<unknown>(this.attempt(scope, order, attempt) + '/simulator/capture',
      { outcome: 'Captured' }, this.options(scope)));
  }

  private async checked(scope: CheckoutScope, order: string, request: Observable<unknown>): Promise<CheckoutPayment> {
    const value = await firstValueFrom(request);
    if (!isCheckoutPayment(value, scope.account, order)) throw new Error('Invalid checkout payment');
    return value;
  }

  private attempt(scope: CheckoutScope, order: string, attempt: string) {
    return this.path(scope, order) + '/attempts/' + encodeURIComponent(attempt);
  }

  private path(scope: CheckoutScope, order: string) {
    return API_BASE_URL + '/billing/accounts/' + encodeURIComponent(scope.account) + '/orders/' +
      encodeURIComponent(order) + '/payment';
  }

  private options(scope: CheckoutScope) {
    return { context: scopedRequestContext(scope) };
  }
}
