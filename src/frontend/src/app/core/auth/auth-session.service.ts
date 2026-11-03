import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';

import { catchError, map, Observable, of, switchMap, tap } from 'rxjs';

import { AUTH_ENDPOINTS } from './auth.constants';
import {
  AccessScopeResult,
  AuthResult,
  AuthSession,
  AuthUser,
  ShopPreferenceResult,
  BootstrapSessionStatus,
  LoginRequest,
  RegisterWithEmailRequest,
  AcceptedMessage,
  ConfirmEmailEnrollmentRequest,
  ConfirmPhoneEnrollmentRequest,
  PhoneAuthAvailability,
  PhoneChallengeReceipt,
  ReauthenticationProof,
  SignInMethods,
} from './auth.models';
import { AuthStorage } from './auth.storage';
import { AuthSessionState, AuthTokenService } from './auth-token.service';
import { LocalizationService } from '../i18n/localization.service';
import { DEFAULT_LANGUAGE } from '../i18n/language.constants';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../storage/offline-sales-snapshot-indexeddb.service';
import { ShopContextService } from '../context/shop-context.service';
import { ScopeChangeReason, ownerOfSession } from '../context/shop-scope';

const CLOCK_SKEW_BUFFER_MS = 30_000;
const OFFLINE_AUTH_GRACE_WINDOW_MS = 48 * 60 * 60 * 1000;

type SessionBroadcastMessage =
  | { readonly type: 'SESSION_UPDATED'; readonly session: AuthSession }
  | { readonly type: 'SESSION_CLEARED' };

/** The device session a scope change was requested from; its answer applies to no other. */
export interface SessionIdentity {
  readonly userId: string;
  readonly sessionId: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly storage = inject(AuthStorage);
  private readonly tokenService = inject(AuthTokenService);
  private readonly localizationService = inject(LocalizationService);
  private readonly offlineSalesDeviceSettingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly offlineSalesSnapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly shopContext = inject(ShopContextService);

  private readonly sessionSignal = signal<AuthSession | null>(null);
  private sessionChannel: BroadcastChannel | null = null;

  readonly session = computed(() => this.sessionSignal());
  readonly isAuthenticated = computed(() => {
    const session = this.sessionSignal();
    return (
      !!session && !this.tokenService.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)
    );
  });
  readonly needsShopSetup = computed(() => {
    const session = this.sessionSignal();
    if (
      !session ||
      this.tokenService.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)
    ) {
      return false;
    }

    const hasAssignedShop = !!session.activeShopId || session.shops.length > 0;
    return !hasAssignedShop;
  });

  constructor() {
    const restored = this.storage.loadSession();
    this.sessionSignal.set(restored);
    this.shopContext.initialize(ownerOfSession(restored));
    this.sessionChannel = new BroadcastChannel('intelibill.auth.session');
    this.sessionChannel.addEventListener(
      'message',
      (event: MessageEvent<SessionBroadcastMessage>) => {
        this.handleSessionBroadcast(event.data);
      },
    );
    this.tokenService.scheduleProactiveRefresh(this.tokenState(), this.sessionSignal());
  }

  login(identifier: string, password: string, rememberMe: boolean): Observable<AuthSession> {
    const trimmedIdentifier = identifier.trim();
    const payload: LoginRequest = { identifier: trimmedIdentifier, password };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.login, payload).pipe(
      map((result) => this.toSession(result, rememberMe)),
      tap((session) => {
        this.setSession(session);
        if (rememberMe) {
          this.storage.saveLastIdentifier(trimmedIdentifier);
        } else {
          this.storage.clearLastIdentifier();
        }
      }),
    );
  }

  registerWithEmail(email: string, claim = false): Observable<AcceptedMessage> {
    const payload: RegisterWithEmailRequest = { email: email.trim() };
    return this.http.post<AcceptedMessage>(
      claim ? AUTH_ENDPOINTS.requestEmailClaim : AUTH_ENDPOINTS.requestEmailEnrollment,
      payload,
    );
  }

  confirmEmailEnrollment(
    payload: ConfirmEmailEnrollmentRequest,
    claim = false,
  ): Observable<AuthSession> {
    return this.http
      .post<AuthResult>(
        claim ? AUTH_ENDPOINTS.confirmEmailClaim : AUTH_ENDPOINTS.confirmEmailEnrollment,
        payload,
      )
      .pipe(
        map((result) => this.toSession(result, false)),
        tap((session) => this.setSession(session)),
      );
  }

  getSignInMethods(): Observable<SignInMethods> {
    return this.http.get<SignInMethods>(AUTH_ENDPOINTS.signInMethods);
  }

  /** Whether this deployment offers phone sign-in, so the option can be hidden rather than fail. */
  getPhoneAuthAvailability(): Observable<PhoneAuthAvailability> {
    return this.http.get<PhoneAuthAvailability>(AUTH_ENDPOINTS.phoneAvailability);
  }

  requestPhoneChallenge(
    phoneNumber: string,
    purpose: 'enrollment' | 'login',
  ): Observable<PhoneChallengeReceipt> {
    return this.http.post<PhoneChallengeReceipt>(
      purpose === 'login' ? AUTH_ENDPOINTS.requestPhoneLogin : AUTH_ENDPOINTS.requestPhoneEnrollment,
      { phoneNumber: phoneNumber.trim() },
    );
  }

  confirmPhoneEnrollment(payload: ConfirmPhoneEnrollmentRequest): Observable<AuthSession> {
    return this.http.post<AuthResult>(AUTH_ENDPOINTS.confirmPhoneEnrollment, payload).pipe(
      map((result) => this.toSession(result, false)),
      tap((session) => this.setSession(session)),
    );
  }

  confirmPhoneLogin(challengeId: string, code: string, rememberMe = true): Observable<AuthSession> {
    return this.http
      .post<AuthResult>(AUTH_ENDPOINTS.confirmPhoneLogin, { challengeId, code })
      .pipe(
        map((result) => this.toSession(result, rememberMe)),
        tap((session) => this.setSession(session)),
      );
  }

  requestPhoneChange(phoneNumber: string, reauthProof: string | null): Observable<PhoneChallengeReceipt> {
    return this.http.post<PhoneChallengeReceipt>(AUTH_ENDPOINTS.requestPhoneChange, {
      phoneNumber: phoneNumber.trim(),
      reauthProof,
    });
  }

  confirmPhoneChange(challengeId: string, code: string): Observable<AcceptedMessage> {
    return this.http.post<AcceptedMessage>(AUTH_ENDPOINTS.confirmPhoneChange, { challengeId, code });
  }

  reauthenticate(password: string): Observable<ReauthenticationProof> {
    return this.http.post<ReauthenticationProof>(AUTH_ENDPOINTS.reauthenticate, { password });
  }

  requestEmailChange(email: string, reauthProof: string): Observable<AcceptedMessage> {
    return this.http.post<AcceptedMessage>(AUTH_ENDPOINTS.requestEmailChange, {
      email: email.trim(),
      reauthProof,
    });
  }

  confirmEmailChange(challengeId: string, token: string): Observable<AcceptedMessage> {
    return this.http
      .post<AcceptedMessage>(AUTH_ENDPOINTS.confirmEmailChange, { challengeId, token })
      .pipe(tap(() => this.clearSession()));
  }

  bootstrapSession(): Observable<boolean> {
    return this.bootstrapSessionWithStatus().pipe(map((status) => status === 'READY'));
  }

  bootstrapSessionWithStatus(): Observable<BootstrapSessionStatus> {
    return this.tokenService.bootstrapSessionWithStatus(this.tokenState());
  }

  refreshAccessToken(options?: {
    readonly preserveSessionOnError?: boolean;
  }): Observable<AuthSession | null> {
    return this.tokenService.refreshAccessToken(this.tokenState(), options);
  }

  signOut(): Observable<void> {
    const refreshToken = this.sessionSignal()?.refreshToken;
    if (!refreshToken) {
      this.clearSession();
      return of(void 0);
    }

    return this.tokenService.revokeToken(refreshToken).pipe(
      catchError(() => of(void 0)),
      tap(() => this.clearSession()),
    );
  }

  signOutAndRedirect(): Observable<void> {
    return this.signOut().pipe(
      switchMap(() => this.router.navigateByUrl('/login')),
      map(() => void 0),
    );
  }

  requestPasswordReset(email: string): Observable<void> {
    return this.http.post<void>(AUTH_ENDPOINTS.requestPasswordReset, { email: email.trim() });
  }

  resetPassword(email: string, token: string, newPassword: string): Observable<void> {
    return this.http.post<void>(AUTH_ENDPOINTS.confirmPasswordReset, { email, token, newPassword });
  }

  getAccessToken(): string {
    return this.sessionSignal()?.accessToken ?? '';
  }

  hasRefreshToken(): boolean {
    const session = this.sessionSignal();
    return !!session?.refreshToken && !this.tokenService.isExpired(session.refreshTokenExpiresAt);
  }

  async canUseOfflineSalesAuthGrace(): Promise<boolean> {
    const session = this.sessionSignal();
    if (!session?.activeShopId) {
      return false;
    }

    if (this.tokenService.isExpired(session.refreshTokenExpiresAt)) {
      return false;
    }

    const activeRole = session.shops
      .find((shop) => shop.shopId === session.activeShopId)
      ?.role?.trim();
    if (!activeRole) {
      return false;
    }

    const settings = this.offlineSalesDeviceSettingsStorage.loadSettings(session.activeShopId);
    if (!settings?.enabled) {
      return false;
    }

    const usable = await this.offlineSalesSnapshotDb.getUsableSnapshotInfo(session.activeShopId);
    if (!usable?.snapshotId || !usable.completedAt) {
      return false;
    }

    const verifiedAt = Date.parse(settings.lastApiVerifiedAt ?? '');
    if (!Number.isFinite(verifiedAt)) {
      return false;
    }

    return Date.now() - verifiedAt <= OFFLINE_AUTH_GRACE_WINDOW_MS;
  }

  getLastRememberedIdentifier(): string {
    return this.storage.getLastIdentifier();
  }

  clearSession(): void {
    this.shopContext.transition(null, 'sign-out', () => {
      this.storage.clearSession();
      this.sessionChannel?.postMessage({ type: 'SESSION_CLEARED' });

      this.tokenService.cancelProactiveRefresh();
      this.sessionSignal.set(null);
    });
  }

  applyAuthResult(result: AuthResult): void {
    this.setSession(this.toSession(result, this.sessionSignal()?.rememberMe ?? true));
  }

  /** The device session this tab holds now, to pass back to `applyAccessScope`. */
  sessionIdentity(): SessionIdentity | null {
    const session = this.sessionSignal();
    return session ? { userId: session.user.id, sessionId: session.sessionId ?? null } : null;
  }

  /**
   * Adopts a scope change for this device. Switching or creating a shop is not a credential event,
   * so the refresh token this sign-in earned is kept exactly as it is.
   *
   * Refused (false) when the tab now holds another device session than `requestedFrom`, or when the
   * server already issued this session a newer scope than the one answered: a late answer to an
   * earlier switch must not move the tab back.
   */
  applyAccessScope(result: AccessScopeResult, requestedFrom?: SessionIdentity | null): boolean {
    const current = this.sessionSignal();
    if (!current) {
      return false;
    }

    if (
      requestedFrom &&
      (requestedFrom.userId !== current.user.id || requestedFrom.sessionId !== (current.sessionId ?? null))
    ) {
      return false;
    }

    const installedVersion = readScopeVersion(current.accessToken);
    if (installedVersion !== null && Number(result.scopeVersion) < installedVersion) {
      return false;
    }

    this.setSession(
      {
        ...current,
        accessToken: result.accessToken,
        accessTokenExpiresAt: result.accessTokenExpiresAt,
        activeShopId: result.activeShopId,
        shops: result.shops ?? [],
      },
      'scope',
    );
    return true;
  }

  /** Adopts a saved shop preference. The shop this device is working in does not move. */
  applyShopPreference(result: ShopPreferenceResult): void {
    const current = this.sessionSignal();
    if (!current) {
      return;
    }

    this.setSession({ ...current, shops: result.shops ?? [] }, 'scope');
  }

  /** Adopts a saved profile. Saving a name issues no token, so none is replaced. */
  applyUser(user: AuthUser): void {
    const current = this.sessionSignal();
    if (!current) {
      return;
    }

    this.setSession(
      {
        ...current,
        user: { ...user, language: user.language || DEFAULT_LANGUAGE },
      },
      'scope',
    );
  }

  /** Every session this tab installs passes through the shop-context transition. */
  private setSession(session: AuthSession, reason: ScopeChangeReason = 'sign-in'): void {
    this.shopContext.transition(ownerOfSession(session), reason, () => {
      this.sessionSignal.set(session);
      const preferredLanguage = session.user.language || DEFAULT_LANGUAGE;

      void this.localizationService.setLanguage(preferredLanguage);

      this.storage.saveSession(session);
      this.sessionChannel?.postMessage({ type: 'SESSION_UPDATED', session });
      this.tokenService.scheduleProactiveRefresh(this.tokenState(), session);
    });
  }

  private toSession(result: AuthResult, rememberMe: boolean): AuthSession {
    const normalizedUser = { ...result.user, language: result.user.language || DEFAULT_LANGUAGE };

    return {
      accessToken: result.accessToken,
      refreshToken: result.refreshToken,
      accessTokenExpiresAt: result.accessTokenExpiresAt,
      refreshTokenExpiresAt: result.refreshTokenExpiresAt,
      user: normalizedUser,
      rememberMe,
      activeShopId: result.activeShopId,
      shops: result.shops ?? [],
      sessionId: result.sessionId ?? null,
    };
  }

  private handleSessionBroadcast(message: SessionBroadcastMessage): void {
    if (message.type === 'SESSION_UPDATED') {
      // Another tab refreshed, switched shop or signed in — adopt the new session so we don't
      // refresh with a revoked token. A different user or shop moves this tab with it.
      this.shopContext.transition(ownerOfSession(message.session), 'cross-tab', () => {
        this.sessionSignal.set(message.session);
        this.storage.saveSession(message.session);
        this.tokenService.scheduleProactiveRefresh(this.tokenState(), message.session);
      });
    } else if (message.type === 'SESSION_CLEARED') {
      // Another tab signed out — mirror the logout in this tab.
      const wasAuthenticated = this.sessionSignal() !== null;
      this.shopContext.transition(null, 'cross-tab', () => {
        this.sessionSignal.set(null);
        this.storage.clearSession();
        this.tokenService.cancelProactiveRefresh();
      });
      if (wasAuthenticated) {
        void this.router.navigateByUrl('/login');
      }
    }
  }

  private tokenState(): AuthSessionState {
    return {
      getSession: () => this.sessionSignal(),
      setSession: (session) => this.setSession(session, 'refresh'),
      clearSession: () => this.clearSession(),
      toSession: (result, rememberMe) => this.toSession(result, rememberMe),
    };
  }
}

/**
 * The `scope_version` claim of an access token, or null when the token carries none. The server
 * raises it on every scope change of a device session, so it orders two scope answers.
 */
export function readScopeVersion(accessToken: string): number | null {
  const payload = accessToken.split('.')[1];
  if (!payload) {
    return null;
  }

  try {
    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
    const claims = JSON.parse(atob(normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '='))) as Record<string, unknown>;
    const version = Number(claims['scope_version']);
    return Number.isFinite(version) ? version : null;
  } catch {
    return null;
  }
}
