import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { BillingAccount, BillingAccountRole } from '../services/billing-account.models';

export interface BillingInvitation {
  readonly invitationId: string;
  readonly accountId: string;
  readonly accountName: string;
  readonly revision: number;
  readonly expiresAt: string;
}

export interface BillingMember {
  readonly memberId: string;
  readonly role: BillingAccountRole;
  readonly isCurrent: boolean;
  readonly revision: number;
}

export interface BillingRoster {
  readonly accountName: string;
  readonly members: readonly BillingMember[];
  readonly ownerRevision: number | null;
  readonly nextCursor: string | null;
}

export interface IssueInvitation {
  readonly operationKey: string;
  readonly expectedOwnerRevision: number;
  readonly recipientKind: 'email' | 'phone';
  readonly recipient: string;
}

export interface RemoveManager {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly reason: string;
}

const billing = `${API_BASE_URL}/billing`;

@Injectable({ providedIn: 'root' })
export class BillingMembersService {
  private readonly http = inject(HttpClient);

  inbox(after?: string) {
    return this.http.get<{ invitations: readonly BillingInvitation[]; nextCursor: string | null }>(
      `${billing}/invitations`, { params: after ? { after } : {} },
    );
  }

  roster(accountId: string, after?: string) {
    return this.http.get<BillingRoster>(
      `${billing}/accounts/${encodeURIComponent(accountId)}/members`, { params: after ? { after } : {} },
    );
  }

  issue(accountId: string, invitation: IssueInvitation) {
    return this.http.post<{ operationKey: string }>(
      `${billing}/accounts/${encodeURIComponent(accountId)}/invitations`, invitation,
    );
  }

  remove(accountId: string, memberId: string, request: RemoveManager) {
    return this.http.post(
      `${billing}/accounts/${encodeURIComponent(accountId)}/members/${encodeURIComponent(memberId)}/remove`, request,
    );
  }

  accept(invitation: BillingInvitation) {
    return this.http.post<BillingAccount>(
      `${billing}/invitations/${encodeURIComponent(invitation.invitationId)}/accept`,
      { expectedInvitationRevision: invitation.revision },
    );
  }
}
