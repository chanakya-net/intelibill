import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/auth/auth.constants';

export const OWNERSHIP_POLICY = 'prospective-billing-owner/1';

export interface OwnershipIssue {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly recipientKind: 'email' | 'phone';
  readonly recipient: string;
  readonly reason: string;
  readonly confirmTransfer: true;
}

export interface OwnershipConsent {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly responsibilityPolicyVersion: typeof OWNERSHIP_POLICY;
  readonly acceptResponsibility: true;
}

export interface OwnershipProposal {
  readonly transferId: string;
  readonly accountId: string;
  readonly accountName: string;
  readonly ownerActorId: string;
  readonly recipientActorId: string;
  readonly revision: number;
  readonly responsibilityPolicyVersion: typeof OWNERSHIP_POLICY;
  readonly expiresAt: string;
}

interface Acknowledgement {
  readonly operationKey: string;
}

export interface OwnershipOutcome extends Acknowledgement {
  readonly accountId: string;
  readonly transferId: string;
  readonly effectiveAt: string;
  readonly role: 'Owner';
}

export type OwnershipDraft =
  | { readonly action: 'issue'; readonly target: string; readonly accountId: string; readonly body: OwnershipIssue }
  | { readonly action: 'accept'; readonly target: string; readonly accountId: string; readonly body: OwnershipConsent };

const billing = `${API_BASE_URL}/billing`;
const proposals = `${billing}/ownership-transfers`;

@Injectable({ providedIn: 'root' })
export class BillingOwnershipTransfers {
  private readonly http = inject(HttpClient);

  inbox(after?: string) {
    return this.http.get<{ transfers: readonly OwnershipProposal[]; nextCursor: string | null }>(
      proposals, { params: after ? { after } : {} },
    );
  }

  preview(id: string) {
    return this.http.get<OwnershipProposal>(`${proposals}/${encodeURIComponent(id)}`);
  }

  send(draft: OwnershipDraft): Observable<Acknowledgement | OwnershipOutcome> {
    const target = encodeURIComponent(draft.target);
    return draft.action === 'issue'
      ? this.http.post<Acknowledgement>(`${billing}/accounts/${target}/ownership-transfers`, draft.body)
      : this.http.post<OwnershipOutcome>(`${proposals}/${target}/accept`, draft.body);
  }

  recover(draft: OwnershipDraft) {
    return this.http.get<Acknowledgement | OwnershipOutcome>(`${billing}/ownership-transfer-operations/${draft.action}/${encodeURIComponent(draft.body.operationKey)}`);
  }
}
