import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';

import { catchError, finalize, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';

import { AUTH_ENDPOINTS } from './auth.constants';
import { AuthResult, AuthSession, LoginWithEmailRequest, RefreshTokenRequest, RegisterWithEmailRequest } from './auth.models';
import { AuthStorage } from './auth.storage';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly storage = inject(AuthStorage);
  private readonly platformId = inject(PLATFORM_ID);

  private readonly sessionSignal = signal<AuthSession | null>(null);
  private refreshInFlight$: Observable<AuthSession | null> | null = null;
  private bootstrapInFlight$: Observable<boolean> | null = null;

  readonly session = computed(() => this.sessionSignal());
  readonly isAuthenticated = computed(() => {
    const session = this.sessionSignal();
    return !!session && !this.isExpired(session.accessTokenExpiresAt);
  });

  constructor() {
    if (this.isBrowser()) {
      this.sessionSignal.set(this.storage.loadSession());
    }
  }

  loginWithEmail(email: string, password: string, rememberMe: boolean): Observable<AuthSession> {
    const payload: LoginWithEmailRequest = { email, password };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.loginWithEmail, payload).pipe(
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

    if (!this.isExpired(session.accessTokenExpiresAt)) {
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
      map((refreshedSession) => refreshedSession),
      catchError((error) => {
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
    }

    this.sessionSignal.set(null);
  }

  private setSession(session: AuthSession): void {
    this.sessionSignal.set(session);

    if (this.isBrowser()) {
      this.storage.saveSession(session);
    }
  }

  private toSession(result: AuthResult, rememberMe: boolean): AuthSession {
    return {
      accessToken: result.accessToken,
      refreshToken: result.refreshToken,
      accessTokenExpiresAt: result.accessTokenExpiresAt,
      refreshTokenExpiresAt: result.refreshTokenExpiresAt,
      user: result.user,
      rememberMe,
    };
  }

  private isExpired(timestamp: string): boolean {
    const expirationTime = Date.parse(timestamp);

    if (Number.isNaN(expirationTime)) {
      return true;
    }

    return expirationTime <= Date.now();
  }

  private isBrowser(): boolean {
    return isPlatformBrowser(this.platformId);
  }
}
