import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

interface RequestContext {
  requestToken?: string;
}

interface AcceptanceContext extends RequestContext {
  available: boolean;
}

export interface IssuedInvitation {
  invitationId: string;
  expiresAt: string;
}

interface PendingEnrollment {
  state: 'enrollmentRequired';
  expiresAt: string;
}

/**
 * Provider administrator invitations. Every write carries the request token from its context
 * read; the invitation token travels only in a POST body, never in a request URL.
 */
@Injectable({ providedIn: 'root' })
export class AdminInvitationsApi {
  private readonly http = inject(HttpClient);
  private readonly ownerPath = '/api/admin/invitations';
  private readonly acceptancePath = '/api/admin/auth/invitation';

  ownerContext() {
    return this.http.get<RequestContext>(this.ownerPath + '/context');
  }

  invite(requestToken: string, email: string) {
    return this.http.post<IssuedInvitation>(this.ownerPath, { email }, this.csrf(requestToken));
  }

  acceptanceContext() {
    return this.http.get<AcceptanceContext>(this.acceptancePath + '/context');
  }

  accept(requestToken: string, invitationToken: string, email: string, password: string) {
    return this.http.post<PendingEnrollment>(
      this.acceptancePath + '/accept',
      { invitationToken, email, password },
      this.csrf(requestToken),
    );
  }

  enrollment() {
    return this.http.get<PendingEnrollment>('/api/admin/auth/bootstrap/enrollment');
  }

  private csrf(token: string) {
    return { headers: { 'X-Bootstrap-CSRF': token } };
  }
}
