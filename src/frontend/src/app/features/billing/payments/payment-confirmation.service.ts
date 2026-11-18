import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { CheckoutScope } from '../checkout/checkout.models';
import { confirmations, MAX_CONFIRMATION_BYTES } from './payment-confirmation.models';

@Injectable({ providedIn: 'root' })
export class PaymentConfirmationService {
  private readonly http = inject(HttpClient);
  async list(scope: CheckoutScope, order: string, after?: string) {
    const value = await firstValueFrom(this.http.get<unknown>(this.path(scope, order) + (after ? '/after/' + encodeURIComponent(after) : ''), { context: scopedRequestContext(scope) }));
    return confirmations(value, scope.account, order);
  }
  async document(scope: CheckoutScope, order: string, id: string) {
    const response = await firstValueFrom(this.http.get(this.path(scope, order) + '/' + encodeURIComponent(id) + '/document',
      { context: scopedRequestContext(scope), responseType: 'blob', observe: 'response' }));
    if (!response.body || response.body.type.split(';')[0] !== 'text/plain' || response.body.size > MAX_CONFIRMATION_BYTES)
      throw new Error('Invalid payment confirmation attachment');
    return response.body;
  }
  private path(scope: CheckoutScope, order: string) {
    return API_BASE_URL + '/billing/accounts/' + encodeURIComponent(scope.account) + '/orders/' + encodeURIComponent(order) + '/payment/confirmations';
  }
}
