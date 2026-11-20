import { HttpClient } from '@angular/common/http';
import { Injectable, Signal, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';

import { API_BASE_URL } from '../auth/auth.constants';
import { ShopContextService } from '../context/shop-context.service';
import { scopedRequestContext } from '../context/scoped-request';

type SettlementPreview =
  | { status: 'loading' | 'unavailable' }
  | { status: 'ready'; limit: number | null };

export const SETTLEMENT_ERROR_KEYS: Readonly<Record<string, string>> = {
  'Payment.SettlementLimitExceeded': 'paymentSettlement.changed',
  'Payment.SettlementSourceIneligible': 'paymentSettlement.sourceIneligible',
};

@Injectable({ providedIn: 'root' })
export class PaymentSettlementPreviewService {
  private readonly http = inject(HttpClient);
  private readonly context = inject(ShopContextService);

  watch(
    partyType: 'Customer' | 'Supplier',
    party: Signal<string>,
    date: Signal<string>,
    refresh: Signal<unknown>,
  ): Signal<SettlementPreview> {
    const request = computed(() => {
      refresh();
      const scope = this.context.scope();
      const partyId = party();
      const paymentDate = date();
      return scope && partyId && paymentDate ? { scope, partyId, paymentDate } : undefined;
    });
    const preview = rxResource({
      params: request,
      stream: ({ params }) =>
        this.http
          .get<{ remainingSettlementDue?: number | null }>(`${API_BASE_URL}/payments/balances`, {
            params: { partyType, partyId: params.partyId, paymentDate: params.paymentDate },
            context: scopedRequestContext(params.scope),
          })
          .pipe(
            map((response) => ({
              request: params,
              result: { status: 'ready', limit: response.remainingSettlementDue ?? null } as const,
            })),
            catchError(() => of({ request: params, result: { status: 'unavailable' } as const })),
          ),
    });
    return computed(() => {
      if (!party()) return { status: 'ready', limit: null };
      const value = preview.value();
      return value && value.request === request() ? value.result : { status: 'loading' };
    });
  }
}

export function canSubmitPayment(preview: SettlementPreview, amount: number): boolean {
  return (
    preview.status === 'ready' &&
    (preview.limit === null || (preview.limit > 0 && amount <= preview.limit))
  );
}

/** The merchant API uses Asia/Kolkata as the shop business zone. */
export function shopPaymentDate(date = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(date);
}
