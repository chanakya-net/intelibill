import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

interface PendingEnrollment {
  state: 'enrollmentRequired';
  expiresAt: string;
}

export type RecoveryProof =
  | { kind: 'code'; recoveryCode: string; continuationKey?: string }
  | { kind: 'reset'; resetToken: string }
  | { kind: 'continue'; continuationKey: string };

export interface RecoveryReview {
  email: string;
  reason: 'authenticator-lost' | 'recovery-codes-lost' | 'suspected-compromise';
  verification: 'video-call' | 'in-person' | 'verified-callback';
  evidenceReference: string;
  confirmed: boolean;
}

export interface IssuedRecoveryReset {
  recoveryId: string;
  resetToken: string;
  expiresAt: string;
}

/**
 * Administrator recovery. Both redemptions need the account password plus a one-use proof and only
 * ever return an enrollment credential; the proof travels in a POST body, never in a URL.
 */
@Injectable({ providedIn: 'root' })
export class AdminRecoveryApi {
  private readonly http = inject(HttpClient);

  /** The provider sign-in request token, shared by every provider auth write. */
  context() {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context');
  }

  redeem(requestToken: string, email: string, password: string, proof: RecoveryProof) {
    const { kind, ...details } = proof;
    const body = { email, password, ...details };
    return this.http.post<PendingEnrollment>(
      `/api/admin/auth/recovery/${kind}`, body, this.csrf(requestToken),
    );
  }

  review(requestToken: string, review: RecoveryReview) {
    return this.http.post<IssuedRecoveryReset>('/api/admin/recovery/resets', review, this.csrf(requestToken));
  }

  private csrf(token: string) {
    return { headers: { 'X-Admin-CSRF': token } };
  }
}
