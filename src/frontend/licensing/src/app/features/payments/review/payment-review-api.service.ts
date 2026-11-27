import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, switchMap } from 'rxjs';

export type ReviewAction = 'RecordNoAccessDecision' | 'ReferForRefundReview' | 'RecoverAcceptedTerms';

export interface ReviewCommand {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly action: ReviewAction;
  readonly reason: string;
  readonly evidence: string;
  readonly sourceHash: string | null;
}

export interface ReviewCase {
  readonly caseId: string;
  readonly orderId: string;
  readonly attemptId: string | null;
  readonly manualReceiptId: string | null;
  readonly inboxId: string | null;
  readonly classification: string;
  readonly disposition: string | null;
  readonly expectedAmountPaise: number;
  readonly expectedCurrency: string;
  readonly observedAmountPaise: number;
  readonly observedCurrency: string;
  readonly providerId: string;
  readonly receivingAccountId: string;
  readonly environment: string;
  readonly configurationVersion: string;
  readonly observedAt: string;
  readonly capturedAt: string | null;
  readonly createdAt: string | null;
}

export interface ReviewDecision {
  readonly caseId: string;
  readonly operationKey: string;
  readonly adminId: string;
  readonly decidedAt: string;
  readonly disposition: string;
  readonly reason: string;
  readonly evidence: string;
  readonly termId: string | null;
}

export interface ReviewDetail {
  readonly case: ReviewCase;
  readonly version: number;
  readonly decision: ReviewDecision | null;
  readonly order: { readonly planName: string; readonly period: string; readonly shopCount: number };
  readonly shops: { readonly items: readonly { shopId: string; name: string }[]; readonly nextCursor: string | null };
  readonly expectedReceivingAccount: string;
  readonly expectedEnvironment: string;
  readonly expectedConfigurationVersion: string;
  readonly recoverySourceHash: string | null;
  readonly recoveryBlocker: string | null;
  readonly recoveryStartsAt: string | null;
  readonly recoveryEndsAt: string | null;
  readonly references: { readonly expectedOrder: string | null; readonly observedOrder: string | null;
    readonly observedPayment: string | null; readonly verifiedAmountPaise: number | null;
    readonly verifiedCurrency: string | null; readonly verifiedAt: string | null };
}

@Injectable({ providedIn: 'root' })
export class PaymentReviewApi {
  private readonly http = inject(HttpClient);

  list(account: string, session: string, order: string, after?: string) {
    return firstValueFrom(this.http.get<ReviewCase[]>(this.path(account), {
      headers: { 'X-Admin-Session': session }, params: { ...(order ? { order } : {}), ...(after ? { after } : {}) },
    }));
  }

  read(account: string, id: string, session: string, after?: string) {
    return firstValueFrom(this.http.get<ReviewDetail>(this.path(account, id), {
      headers: { 'X-Admin-Session': session }, params: after ? { after } : {},
    }));
  }

  recover(account: string, id: string, session: string, key: string) {
    return firstValueFrom(this.http.get<ReviewDecision>(this.path(account, id) + '/operations/' + encodeURIComponent(key),
      { headers: { 'X-Admin-Session': session } }));
  }

  decide(account: string, id: string, session: string, body: ReviewCommand) {
    return firstValueFrom(this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<ReviewDecision>(this.path(account, id) + '/decisions', body,
        { headers: { 'X-Admin-Session': session, 'X-Admin-CSRF': requestToken } })),
    ));
  }

  private path(account: string, id?: string) {
    return `/api/admin/accounts/${encodeURIComponent(account)}/payment-reviews${id ? '/' + encodeURIComponent(id) : ''}`;
  }
}
