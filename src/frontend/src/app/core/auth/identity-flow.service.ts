import { Injectable, inject } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ReauthenticationProof } from './auth.models';
import { ACCEPT_INVITATION_ROUTE, readPendingInvitation } from './pending-invitation';

export type IdentityOperation = { kind: 'email'; email: string };
export type IdentityOutcome = 'email-pending';
export interface EmailVerification {
  challengeId: string;
  token: string;
  purpose: 'enrollment' | 'claim' | 'email-change';
}
const INVALID_CHALLENGE = 'Auth.InvalidChallenge';
const VERIFICATION_KEY = 'intelibill.identity.verification';

@Injectable({ providedIn: 'root' })
export class IdentityFlowService {
  private readonly auth = inject(AuthService);

  saveVerification(proof: EmailVerification): boolean {
    const existing = this.pendingVerification();
    if (existing?.challengeId === proof.challengeId && existing.token === proof.token) return true;
    return this.write(VERIFICATION_KEY, { ...proof, expiresAt: Date.now() + 30 * 60000 });
  }
  pendingVerification(): EmailVerification | null {
    const pending = this.read(VERIFICATION_KEY);
    if (!pending) return null;
    try {
      const data = JSON.parse(pending);
      if (
        data.purpose === 'email-change' &&
        typeof data.challengeId === 'string' &&
        typeof data.token === 'string' &&
        data.challengeId &&
        data.token &&
        data.expiresAt > Date.now() &&
        data.expiresAt <= Date.now() + 30 * 60000
      )
        return data;
    } catch {
      /* Invalid or expired local continuation. */
    }
    this.clearVerification();
    return null;
  }
  clearVerification(): void {
    this.remove(VERIFICATION_KEY);
  }
  afterSignIn(): string {
    if (this.pendingVerification()) {
      return '/auth/verify-email';
    }

    // Someone who opened an invitation link before signing in is returned to it, so the single
    // trip from the email is enough.
    return readPendingInvitation() ? ACCEPT_INVITATION_ROUTE : '/';
  }

  perform(operation: IdentityOperation, proof: ReauthenticationProof): Observable<IdentityOutcome> {
    if (!this.auth.isAuthenticated() || Date.parse(proof.expiresAt) <= Date.now())
      return this.invalid();
    return this.auth
      .requestEmailChange(operation.email, proof.proof)
      .pipe(map(() => 'email-pending' as const));
  }

  private invalid(): Observable<never> {
    return throwError(() => ({ error: { title: INVALID_CHALLENGE } }));
  }
  private read(key: string): string | null {
    try {
      return sessionStorage.getItem(key);
    } catch {
      return null;
    }
  }
  private write(key: string, value: unknown): boolean {
    try {
      sessionStorage.setItem(key, JSON.stringify(value));
      return true;
    } catch {
      return false;
    }
  }
  private remove(key: string): void {
    try {
      sessionStorage.removeItem(key);
    } catch {
      /* No durable storage available. */
    }
  }
}
