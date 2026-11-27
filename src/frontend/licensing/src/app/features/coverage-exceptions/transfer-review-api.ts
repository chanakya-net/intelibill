import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, switchMap } from 'rxjs';
import { PaymentDraftStore } from '../payments/manual-payment-drafts';

export interface TransferSummary {
  transferId: string;
  shopName: string;
  destinationAccountId: string;
}
interface Period {
  termId: string;
  startsAt: string;
  paidThroughAt: string | null;
}
export interface TransferReview extends TransferSummary {
  shopId: string;
  sourceAccountId: string;
  sourceAccountName: string;
  destinationAccountName: string | null;
  sourceTerms: Period[];
  destinationTerms: Period[];
  cutoverAt: string;
  sourcePaidThroughAt: string;
  memberAllowance: number | null;
  observedPermitCount: number;
  observedPermitDeadline: string | null;
  treatment: string;
  reviewDigest: string;
  consents: { purpose: string; status: string }[];
  blocker: string | null;
  approvalState: string | null;
}
export interface TransferCommand {
  operationKey: string;
  reason: string;
  expectedReviewDigest: string;
  treatment: string;
  cutoverAt: string;
  paidThroughAt: string;
  acknowledgedAllocation: true;
  acknowledgedPermitsAndUnknownQueues: true;
  acknowledgedHistoricalBillsStay: true;
}
export interface TransferReceipt {
  transferId: string;
  operationKey: string;
  state: string;
  outcomeReason: string | null;
}
@Injectable({ providedIn: 'root' })
export class TransferDrafts extends PaymentDraftStore<TransferCommand> {
  constructor() { super('intelibill.admin.coverage-transfers'); }
}

@Injectable({ providedIn: 'root' })
export class TransferReviewApi {
  private readonly http = inject(HttpClient);
  list(account: string, session: string) {
    return firstValueFrom(this.http.get<TransferSummary[]>(this.path(account), { headers: { 'X-Admin-Session': session } }));
  }
  async preview(account: string, id: string, session: string) {
    const result = await this.post<TransferReview>(this.path(account, id) + '/preview', {}, session);
    if (result.sourceAccountId !== account || result.transferId !== id || !/^[a-f0-9]{64}$/.test(result.reviewDigest))
      throw new Error('Transfer review does not match the selected request.');
    return result;
  }
  async decide(account: string, id: string, session: string, cmd: TransferCommand, lookup: boolean) {
    const path = this.path(account, id);
    const receipt = lookup
      ? await firstValueFrom(this.http.get<TransferReceipt>(path + '/operations/' + encodeURIComponent(cmd.operationKey),
          { headers: { 'X-Admin-Session': session } }))
      : await this.post<TransferReceipt>(path + '/approve', cmd, session);
    if (receipt.operationKey !== cmd.operationKey || receipt.transferId !== id)
      throw new Error('Unexpected approval receipt.');
    return receipt;
  }
  private path(account: string, transfer?: string) {
    const base = '/api/admin/accounts/' + encodeURIComponent(account) + '/coverage-transfers';
    return transfer ? base + '/' + encodeURIComponent(transfer) : base;
  }
  private post<T>(path: string, body: object, session: string) {
    return firstValueFrom(this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<T>(path, body,
        { headers: { 'X-Admin-CSRF': requestToken, 'X-Admin-Session': session } })),
    ));
  }
}
