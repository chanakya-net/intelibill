import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';

import { catchError, map, Observable, of, switchMap, tap } from 'rxjs';

import { AUTH_ENDPOINTS } from './auth.constants';
import {
  AuthResult,
  AuthSession,
  BootstrapSessionStatus,
  ExternalAuthProvider,
  ExternalLoginCallbackRequest,
  ExternalLoginInitRequest,
  ExternalLoginInitResponse,
  LoginRequest,
  RegisterWithEmailRequest,
} from './auth.models';
import { AuthStorage } from './auth.storage';
import { AuthSessionState, AuthTokenService } from './auth-token.service';
import { LocalizationService } from '../i18n/localization.service';
import { DEFAULT_LANGUAGE } from '../i18n/language.constants';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../storage/offline-sales-snapshot-indexeddb.service';

const CLOCK_SKEW_BUFFER_MS = 30_000;
const OFFLINE_AUTH_GRACE_WINDOW_MS = 48 * 60 * 60 * 1000;

type SessionBroadcastMessage =
  | { readonly type: 'SESSION_UPDATED'; readonly session: AuthSession }
  | { readonly type: 'SESSION_CLEARED' };

@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly storage = inject(AuthStorage);
  private readonly tokenService = inject(AuthTokenService);
  private readonly localizationService = inject(LocalizationService);
  private readonly offlineSalesDeviceSettingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly offlineSalesSnapshotDb = inject(OfflineSalesSnapshotIndexedDbService);

  private readonly sessionSignal = signal<AuthSession | null>(null);
  private sessionChannel: BroadcastChannel | null = null;

  readonly session = computed(() => this.sessionSignal());
  readonly isAuthenticated = computed(() => {
    const session = this.sessionSignal();
    return !!session && !this.tokenService.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS);
  });
  readonly needsShopSetup = computed(() => {
    const session = this.sessionSignal();
    if (!session || this.tokenService.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)) {
      return false;
    }

    const hasAssignedShop = !!session.activeShopId || session.shops.length > 0;
    return !hasAssignedShop;
  });

  constructor() {
    this.sessionSignal.set(this.storage.loadSession());
    this.sessionChannel = new BroadcastChannel('intelibill.auth.session');
    this.sessionChannel.addEventListener('message', (event: MessageEvent<SessionBroadcastMessage>) => {
      this.handleSessionBroadcast(event.data);
    });
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
      })
    );
  }

  registerWithEmail(
    firstName: string,
    lastName: string,
    email: string,
    phoneNumber: string,
    password: string,
    rememberMe: boolean
  ): Observable<AuthSession> {
    const payload: RegisterWithEmailRequest = { firstName, lastName, email, phoneNumber, password };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.registerWithEmail, payload).pipe(
      map((result) => this.toSession(result, rememberMe)),
      tap((session) => {
        this.setSession(session);
        if (rememberMe) this.storage.saveLastIdentifier(email);
        else this.storage.clearLastIdentifier();
      })
    );
  }

  initializeExternalLogin(provider: ExternalAuthProvider): Observable<string> {
    const payload: ExternalLoginInitRequest = { provider };

    return this.http.post<ExternalLoginInitResponse>(AUTH_ENDPOINTS.loginExternalInit, payload).pipe(
      map((result) => result.authorizationUrl)
    );
  }

  completeExternalLogin(code: string, state: string, firstName?: string, lastName?: string): Observable<AuthSession> {
    const payload: ExternalLoginCallbackRequest = { code, state, firstName, lastName };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.loginExternalCallback, payload).pipe(
      map((result) => this.toSession(result, true)),
      tap((session) => this.setSession(session))
    );
  }

  bootstrapSession(): Observable<boolean> {
    return this.bootstrapSessionWithStatus().pipe(map((status) => status === 'READY'));
  }

  bootstrapSessionWithStatus(): Observable<BootstrapSessionStatus> {
    return this.tokenService.bootstrapSessionWithStatus(this.tokenState());
  }

  refreshAccessToken(options?: { readonly preserveSessionOnError?: boolean }): Observable<AuthSession | null> {
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
      tap(() => this.clearSession())
    );
  }

  signOutAndRedirect(): Observable<void> {
    return this.signOut().pipe(
      switchMap(() => this.router.navigateByUrl('/login')),
      map(() => void 0)
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

    const activeRole = session.shops.find((shop) => shop.shopId === session.activeShopId)?.role?.trim();
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
    this.storage.clearSession();
    this.sessionChannel?.postMessage({ type: 'SESSION_CLEARED' });

    this.tokenService.cancelProactiveRefresh();
    this.sessionSignal.set(null);
  }

  applyAuthResult(result: AuthResult): void {
    this.setSession(this.toSession(result, this.sessionSignal()?.rememberMe ?? true));
  }

  private setSession(session: AuthSession): void {
    this.sessionSignal.set(session);
    const preferredLanguage = session.user.language || DEFAULT_LANGUAGE;

    void this.localizationService.setLanguage(preferredLanguage);

    this.storage.saveSession(session);
    this.sessionChannel?.postMessage({ type: 'SESSION_UPDATED', session });
    this.tokenService.scheduleProactiveRefresh(this.tokenState(), session);
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
    };
  }

  private handleSessionBroadcast(message: SessionBroadcastMessage): void {
    if (message.type === 'SESSION_UPDATED') {
      // Another tab successfully refreshed — adopt the new session so we
      // don't attempt to refresh using the now-revoked token.
      this.sessionSignal.set(message.session);
      this.storage.saveSession(message.session);
      this.tokenService.scheduleProactiveRefresh(this.tokenState(), message.session);
    } else if (message.type === 'SESSION_CLEARED') {
      // Another tab signed out — mirror the logout in this tab.
      const wasAuthenticated = this.sessionSignal() !== null;
      this.sessionSignal.set(null);
      this.storage.clearSession();
      this.tokenService.cancelProactiveRefresh();
      if (wasAuthenticated) {
        void this.router.navigateByUrl('/login');
      }
    }
  }

  private tokenState(): AuthSessionState {
    return {
      getSession: () => this.sessionSignal(),
      setSession: (session) => this.setSession(session),
      clearSession: () => this.clearSession(),
      toSession: (result, rememberMe) => this.toSession(result, rememberMe),
    };
  }

}
