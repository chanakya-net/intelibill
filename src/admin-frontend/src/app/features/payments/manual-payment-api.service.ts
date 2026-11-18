import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, switchMap } from 'rxjs';

export interface PaymentScope {
  readonly admin: string;
  readonly account: string;
  readonly order: string;
}

export interface ManualPaymentCommand {
  readonly operationKey: string;
  readonly expectedRevision: number;
  readonly snapshotHash: string;
  readonly receivingAccountId: string;
  readonly configurationVersion: string;
  readonly reference: string;
  readonly evidence: string;
  readonly receivedAt: string;
  readonly amountPaise: number;
  readonly currency: 'INR';
  readonly reason: string;
  readonly verified: boolean;
}

interface FrozenOrder {
  readonly revision: number;
  readonly snapshotHash: string;
  readonly planName: string;
  readonly period: string;
  readonly amountPaise: number;
  readonly currency: 'INR';
  readonly shopCount: number;
  readonly shops: readonly { shopId: string; name: string }[];
  readonly nextShop: string | null;
}

interface ReceivingAccount {
  readonly id: string;
  readonly label: string;
  readonly environment: string;
  readonly configurationVersion: string;
  readonly evidencePolicyVersion: string;
}

export interface ManualPaymentReceipt {
  readonly receiptId: string;
  readonly operationKey: string;
  readonly adminId: string;
  readonly verifiedAt: string;
  readonly receivedAt: string;
  readonly mfaVerifiedAt: string;
  readonly configurationVersion: string;
  readonly evidencePolicyVersion: string;
  readonly amountPaise: number;
  readonly currency: string;
  readonly source: 'Manual';
  readonly payment: 'Verified';
  readonly fulfilment: 'NeedsReview' | 'Fulfilled';
  readonly reviewReason: string | null;
  readonly termId: string | null;
}

export interface ManualPaymentView {
  readonly order: FrozenOrder;
  readonly receivingAccount: ReceivingAccount | null;
  readonly status: { payment: string; attemptId: string | null; fulfilment: string; access: string; restorationHeld?: boolean };
  readonly onlineFundsNeedReview: boolean;
  readonly receipts: readonly ManualPaymentReceipt[];
}

export interface ManualPaymentEvidence {
  readonly reference: string;
  readonly evidence: string;
  readonly reason: string;
}

export interface ManualPaymentPreview {
  readonly order: FrozenOrder;
  readonly receivingAccount: ReceivingAccount;
  readonly receivedAt: string;
  readonly reviewReason: string | null;
  readonly estimatedStart: string | null;
  readonly estimatedEnd: string | null;
}

export interface ProviderPin {
  readonly providerId: string;
  readonly providerAccountId: string;
  readonly environment: string;
  readonly configurationVersion: string;
}

export interface PaymentRoutingDetail {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly orderId: string;
  readonly attemptId: string | null;
  readonly original: ProviderPin | null;
  readonly currentSelection: { readonly revision: number; readonly profileId: string | null; readonly pin: ProviderPin | null };
  readonly recoveryAvailable: boolean;
  readonly blocker: string | null;
}

export interface ConfirmationReference {
  readonly confirmationId: string;
  readonly accountId: string;
  readonly orderId: string;
  readonly source: 'Online' | 'Manual';
}

@Injectable({ providedIn: 'root' })
export class ManualPaymentApi {
  private readonly http = inject(HttpClient);

  read(scope: PaymentScope, session: string, after?: string) {
    return firstValueFrom(this.http.get<ManualPaymentView>(this.path(scope), {
      headers: { 'X-Admin-Session': session }, params: after ? { after } : {},
    }));
  }

  routing(scope: PaymentScope, session: string) {
    return firstValueFrom(this.http.get<PaymentRoutingDetail>(
      `/api/admin/accounts/${encodeURIComponent(scope.account)}/orders/${encodeURIComponent(scope.order)}/payment-routing`,
      { headers: { 'X-Admin-Session': session } }));
  }

  preview(scope: PaymentScope, session: string, command: ManualPaymentCommand) {
    return this.post<ManualPaymentPreview>(this.path(scope) + '/preview', session, command);
  }

  confirm(scope: PaymentScope, session: string, command: ManualPaymentCommand) {
    return this.post<ManualPaymentReceipt>(this.path(scope), session, command);
  }

  recover(scope: PaymentScope, session: string, key: string) {
    return firstValueFrom(this.http.get<ManualPaymentReceipt>(this.path(scope) + '/operations/' + encodeURIComponent(key),
      { headers: { 'X-Admin-Session': session } }));
  }

  evidence(scope: PaymentScope, session: string, receipt: string) {
    return firstValueFrom(this.http.get<ManualPaymentEvidence>(
      this.path(scope) + '/receipts/' + encodeURIComponent(receipt) + '/evidence', { headers: { 'X-Admin-Session': session } }));
  }

  async confirmations(scope: PaymentScope, session: string) {
    const rows = await firstValueFrom(this.http.get<readonly ConfirmationReference[]>(this.confirmationsPath(scope),
      { headers: { 'X-Admin-Session': session } }));
    if (!Array.isArray(rows) || !rows.every(row => row.accountId === scope.account && row.orderId === scope.order &&
        /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(row.confirmationId) && ['Online', 'Manual'].includes(row.source)))
      throw new Error('Invalid confirmation identity');
    return rows;
  }

  async confirmationDocument(scope: PaymentScope, session: string, id: string) {
    const blob = await firstValueFrom(this.http.get(this.confirmationsPath(scope) + '/' + encodeURIComponent(id) + '/document',
      { headers: { 'X-Admin-Session': session }, responseType: 'blob' }));
    if (blob.type.split(';')[0] !== 'text/plain' || blob.size > 8192 + 100_000 * (250 * 4 + 128)) throw new Error('Invalid confirmation document');
    return blob;
  }

  private confirmationsPath(scope: PaymentScope) {
    return `/api/admin/accounts/${encodeURIComponent(scope.account)}/orders/${encodeURIComponent(scope.order)}/payment-confirmations`;
  }

  private post<T>(path: string, session: string, body: ManualPaymentCommand) {
    return firstValueFrom(this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<T>(path, body,
        { headers: { 'X-Admin-Session': session, 'X-Admin-CSRF': requestToken } })),
    ));
  }

  private path(scope: PaymentScope) {
    return `/api/admin/accounts/${encodeURIComponent(scope.account)}/orders/${encodeURIComponent(scope.order)}/manual-payments`;
  }
}
