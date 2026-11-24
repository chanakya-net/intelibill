import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { CheckoutScope } from '../checkout/checkout.models';
import {
  isValidRefundRequest, matchesRefundCommand, validateRefundPage,
  type CreateRefundRequest, type RefundRequestItem, type RefundRequestPage,
} from './refund-request.models';

@Injectable({ providedIn: 'root' })
export class RefundRequestService {
  private readonly http = inject(HttpClient);

  async create(scope: CheckoutScope, command: CreateRefundRequest): Promise<RefundRequestItem> {
    const value = await firstValueFrom(this.http.post<unknown>(this.path(scope), command,
      { context: scopedRequestContext(scope) }));
    if (!isValidRefundRequest(value, scope.account) || !matchesRefundCommand(value, scope.account, command))
      throw new Error('Invalid refund request response');
    return value;
  }

  async list(scope: CheckoutScope, after?: string): Promise<RefundRequestPage> {
    const value = await firstValueFrom(this.http.get<unknown>(this.path(scope),
      { context: scopedRequestContext(scope), params: after ? { after } : {} }));
    return validateRefundPage(value, scope.account);
  }

  private path(scope: CheckoutScope): string {
    return API_BASE_URL + '/billing/accounts/' + encodeURIComponent(scope.account) + '/refund-requests';
  }
}
