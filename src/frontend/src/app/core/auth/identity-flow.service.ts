import { Injectable, InjectionToken, inject } from '@angular/core';
import { Observable, map, of, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ExternalAuthProvider, ReauthenticationProof } from './auth.models';
import { ACCEPT_INVITATION_ROUTE, readPendingInvitation } from './pending-invitation';

export type IdentityOperation =
  | { kind: 'email'; email: string }
  | { kind: 'link' | 'unlink'; provider: ExternalAuthProvider };
export type IdentityOutcome = 'redirecting' | 'email-pending' | 'security-updated';
export interface EmailVerification {
  challengeId: string;
  token: string;
  purpose: 'enrollment' | 'claim' | 'email-change';
}
interface PendingOAuth {
  mode: 'reauth' | 'link';
  actor: string;
  expiresAt: number;
  state: string;
  operation: IdentityOperation;
  proof?: ReauthenticationProof;
}
export const IDENTITY_REDIRECT = new InjectionToken<(url: string) => void>('IDENTITY_REDIRECT', {
  factory: () => (url) => window.location.assign(url),
});
const INVALID_CHALLENGE = 'Auth.InvalidChallenge';
const OAUTH_KEY = 'intelibill.identity.pending';
const VERIFICATION_KEY = 'intelibill.identity.verification';

@Injectable({ providedIn: 'root' })
export class IdentityFlowService {
  private readonly auth = inject(AuthService);
  private readonly redirect = inject(IDENTITY_REDIRECT);

  hasOAuthIntent(): boolean {
    return !!this.read(OAUTH_KEY);
  }
  clearOAuth(): void {
    this.remove(OAUTH_KEY);
  }
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

  startProviderReauth(
    provider: ExternalAuthProvider,
    operation: IdentityOperation,
  ): Observable<IdentityOutcome> {
    return this.beginOAuth(provider, {
      mode: 'reauth',
      operation,
      actor: this.auth.session()?.user.id ?? '',
      expiresAt: Date.now() + 5 * 60000,
    });
  }

  perform(operation: IdentityOperation, proof: ReauthenticationProof): Observable<IdentityOutcome> {
    if (!this.auth.isAuthenticated() || Date.parse(proof.expiresAt) <= Date.now())
      return this.invalid();
    if (operation.kind === 'email')
      return this.auth
        .requestEmailChange(operation.email, proof.proof)
        .pipe(map(() => 'email-pending'));
    if (operation.kind === 'unlink')
      return this.auth
        .unlinkExternalProvider(operation.provider, proof.proof)
        .pipe(map(() => 'security-updated'));
    return this.beginOAuth(operation.provider, {
      mode: 'link',
      operation,
      proof,
      actor: this.auth.session()?.user.id ?? '',
      expiresAt: Math.min(Date.parse(proof.expiresAt), Date.now() + 5 * 60000),
    });
  }

  completeOAuth(code: string, state: string): Observable<IdentityOutcome> {
    const raw = this.read(OAUTH_KEY);
    this.clearOAuth();
    let context: PendingOAuth;
    try {
      context = JSON.parse(raw ?? 'null');
      if (
        !context ||
        !this.auth.isAuthenticated() ||
        context.actor !== this.auth.session()?.user.id ||
        context.state !== state ||
        context.expiresAt <= Date.now() ||
        context.expiresAt > Date.now() + 5 * 60000 ||
        !['reauth', 'link'].includes(context.mode) ||
        !context.operation ||
        !['email', 'link', 'unlink'].includes(context.operation.kind)
      )
        return this.invalid();
    } catch {
      return this.invalid();
    }
    if (context.mode === 'link') {
      if (!context.proof || Date.parse(context.proof.expiresAt) <= Date.now())
        return this.invalid();
      return this.auth
        .completeExternalLink(code, state, context.proof.proof)
        .pipe(map(() => 'security-updated'));
    }
    return this.auth
      .reauthenticateExternal(code, state)
      .pipe(switchMap((proof) => this.perform(context.operation, proof)));
  }

  private beginOAuth(
    provider: ExternalAuthProvider,
    context: Omit<PendingOAuth, 'state'>,
  ): Observable<IdentityOutcome> {
    if (!context.actor || !this.auth.isAuthenticated()) return this.invalid();
    return this.auth.initializeExternalLogin(provider).pipe(
      switchMap((url) => {
        const state = new URL(url).searchParams.get('state');
        if (!state || !this.write(OAUTH_KEY, { ...context, state })) return this.invalid();
        this.redirect(url);
        return of('redirecting' as const);
      }),
    );
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
