import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';

import { catchError, finalize, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';

import { AUTH_ENDPOINTS } from './auth.constants';
import {
  AuthResult,
  AuthSession,
  ExternalAuthProvider,
  ExternalLoginCallbackRequest,
  ExternalLoginInitRequest,
  ExternalLoginInitResponse,
  LoginRequest,
  RefreshTokenRequest,
  RegisterWithEmailRequest,
} from './auth.models';
import { AuthStorage } from './auth.storage';
import { LocalizationService } from '../i18n/localization.service';
import { DEFAULT_LANGUAGE } from '../i18n/language.constants';

const CLOCK_SKEW_BUFFER_MS = 30_000;
const PROACTIVE_REFRESH_LEAD_MS = 60_000;

type SessionBroadcastMessage =
  | { readonly type: 'SESSION_UPDATED'; readonly session: AuthSession }
  | { readonly type: 'SESSION_CLEARED' };

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly storage = inject(AuthStorage);
  private readonly localizationService = inject(LocalizationService);
  private readonly platformId = inject(PLATFORM_ID);

  private readonly sessionSignal = signal<AuthSession | null>(null);
  private refreshInFlight$: Observable<AuthSession | null> | null = null;
  private bootstrapInFlight$: Observable<boolean> | null = null;
  private sessionChannel: BroadcastChannel | null = null;
  private proactiveRefreshTimerId: ReturnType<typeof setTimeout> | null = null;

  readonly session = computed(() => this.sessionSignal());
  readonly isAuthenticated = computed(() => {
    const session = this.sessionSignal();
    return !!session && !this.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS);
  });
  readonly needsShopSetup = computed(() => {
    const session = this.sessionSignal();
    if (!session || this.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)) {
      return false;
    }

    const hasAssignedShop = !!session.activeShopId || session.shops.length > 0;
    return !hasAssignedShop;
  });

  constructor() {
    if (this.isBrowser()) {
      this.sessionSignal.set(this.storage.loadSession());
      this.sessionChannel = new BroadcastChannel('intelibill.auth.session');
      this.sessionChannel.addEventListener('message', (event: MessageEvent<SessionBroadcastMessage>) => {
        this.handleSessionBroadcast(event.data);
      });
      this.scheduleProactiveRefresh(this.sessionSignal());
    }
  }

  login(identifier: string, password: string, rememberMe: boolean): Observable<AuthSession> {
    const payload: LoginRequest = { identifier, password };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.login, payload).pipe(
      map((result) => this.toSession(result, rememberMe)),
      tap((session) => {
        this.setSession(session);
        if (rememberMe) {
          this.storage.saveLastEmail(identifier);
        } else {
          this.storage.clearLastEmail();
        }
      })
    );
  }

  registerWithEmail(
    firstName: string,
    lastName: string,
    email: string,
    password: string,
    rememberMe: boolean
  ): Observable<AuthSession> {
    const payload: RegisterWithEmailRequest = {
      firstName,
      lastName,
      email,
      password,
    };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.registerWithEmail, payload).pipe(
      map((result) => this.toSession(result, rememberMe)),
      tap((session) => {
        this.setSession(session);

        if (rememberMe) {
          this.storage.saveLastEmail(email);
        } else {
          this.storage.clearLastEmail();
        }
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
    const payload: ExternalLoginCallbackRequest = {
      code,
      state,
      firstName,
      lastName,
    };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.loginExternalCallback, payload).pipe(
      map((result) => this.toSession(result, true)),
      tap((session) => this.setSession(session))
    );
  }

  bootstrapSession(): Observable<boolean> {
    if (!this.isBrowser()) {
      return of(true);
    }

    if (this.bootstrapInFlight$) {
      return this.bootstrapInFlight$;
    }

    const session = this.sessionSignal();
    if (!session) {
      return of(false);
    }

    if (!this.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)) {
      return of(true);
    }

    if (this.isExpired(session.refreshTokenExpiresAt)) {
      this.clearSession();
      return of(false);
    }

    this.bootstrapInFlight$ = this.refreshAccessToken().pipe(
      map((refreshedSession) => !!refreshedSession),
      catchError(() => {
        this.clearSession();
        return of(false);
      }),
      finalize(() => {
        this.bootstrapInFlight$ = null;
      }),
      shareReplay(1)
    );

    return this.bootstrapInFlight$;
  }

  refreshAccessToken(): Observable<AuthSession | null> {
    if (!this.isBrowser()) {
      return of(null);
    }

    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    const session = this.sessionSignal();
    if (!session || this.isExpired(session.refreshTokenExpiresAt)) {
      this.clearSession();
      return of(null);
    }

    const payload: RefreshTokenRequest = { refreshToken: session.refreshToken };

    this.refreshInFlight$ = this.http.post<AuthResult>(AUTH_ENDPOINTS.refreshToken, payload).pipe(
      map((result) => this.toSession(result, session.rememberMe)),
      tap((refreshedSession) => this.setSession(refreshedSession)),
      catchError((error) => {
        // Another tab may have already rotated the token and saved a fresh session
        // to localStorage. Re-read storage before clearing to avoid spurious logout
        // on multi-tab refresh races.
        const fresherSession = this.storage.loadSession();
        if (fresherSession && !this.isExpired(fresherSession.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)) {
          this.sessionSignal.set(fresherSession);
          return of(fresherSession);
        }
        this.clearSession();
        return throwError(() => error);
      }),
      finalize(() => {
        this.refreshInFlight$ = null;
      }),
      shareReplay(1)
    );

    return this.refreshInFlight$;
  }

  signOut(): Observable<void> {
    if (!this.isBrowser()) {
      this.sessionSignal.set(null);
      return of(void 0);
    }

    const refreshToken = this.sessionSignal()?.refreshToken;
    if (!refreshToken) {
      this.clearSession();
      return of(void 0);
    }

    return this.http.post<void>(AUTH_ENDPOINTS.revokeToken, { refreshToken }).pipe(
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

  getAccessToken(): string {
    const token = this.sessionSignal()?.accessToken;
    return token ?? '';
  }

  hasRefreshToken(): boolean {
    const refreshToken = this.sessionSignal()?.refreshToken;
    if (!refreshToken) {
      return false;
    }

    return !this.isExpired(this.sessionSignal()!.refreshTokenExpiresAt);
  }

  getLastRememberedEmail(): string {
    if (!this.isBrowser()) {
      return '';
    }

    return this.storage.getLastEmail();
  }

  clearSession(): void {
    if (this.isBrowser()) {
      this.storage.clearSession();
      this.sessionChannel?.postMessage({ type: 'SESSION_CLEARED' });
    }

    this.cancelProactiveRefresh();
    this.sessionSignal.set(null);
  }

  applyAuthResult(result: AuthResult): void {
    const currentSession = this.sessionSignal();
    const rememberMe = currentSession?.rememberMe ?? true;
    this.setSession(this.toSession(result, rememberMe));
  }

  private setSession(session: AuthSession): void {
    this.sessionSignal.set(session);
    const preferredLanguage = session.user.language || DEFAULT_LANGUAGE;

    void this.localizationService.setLanguage(preferredLanguage);

    if (this.isBrowser()) {
      this.storage.saveSession(session);
      this.sessionChannel?.postMessage({ type: 'SESSION_UPDATED', session });
      this.scheduleProactiveRefresh(session);
    }
  }

  private toSession(result: AuthResult, rememberMe: boolean): AuthSession {
    const normalizedUser = {
      ...result.user,
      language: result.user.language || DEFAULT_LANGUAGE,
    };

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
      this.scheduleProactiveRefresh(message.session);
    } else if (message.type === 'SESSION_CLEARED') {
      // Another tab signed out — mirror the logout in this tab.
      const wasAuthenticated = this.sessionSignal() !== null;
      this.sessionSignal.set(null);
      this.storage.clearSession();
      this.cancelProactiveRefresh();
      if (wasAuthenticated) {
        void this.router.navigateByUrl('/login');
      }
    }
  }

  private scheduleProactiveRefresh(session: AuthSession | null): void {
    this.cancelProactiveRefresh();
    if (!session) return;

    const expiresAt = Date.parse(session.accessTokenExpiresAt);
    if (Number.isNaN(expiresAt)) return;

    const delay = expiresAt - PROACTIVE_REFRESH_LEAD_MS - Date.now();
    if (delay <= 0) return;

    this.proactiveRefreshTimerId = setTimeout(() => {
      this.proactiveRefreshTimerId = null;
      this.refreshAccessToken().subscribe({ error: () => { /* clearSession called internally */ } });
    }, delay);
  }

  private cancelProactiveRefresh(): void {
    if (this.proactiveRefreshTimerId !== null) {
      clearTimeout(this.proactiveRefreshTimerId);
      this.proactiveRefreshTimerId = null;
    }
  }

  private isExpired(timestamp: string, bufferMs = 0): boolean {
    const expirationTime = Date.parse(timestamp);

    if (Number.isNaN(expirationTime)) {
      return true;
    }

    return expirationTime - bufferMs <= Date.now();
  }

  private isBrowser(): boolean {
    return isPlatformBrowser(this.platformId);
  }
}
