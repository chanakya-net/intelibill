import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';

import { catchError, finalize, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';

import { AUTH_ENDPOINTS } from './auth.constants';
import { AuthResult, AuthSession, BootstrapSessionStatus, RefreshTokenRequest } from './auth.models';
import { AuthStorage } from './auth.storage';
import { NetworkStatusService } from '../services/network-status.service';

const CLOCK_SKEW_BUFFER_MS = 30_000;
const PROACTIVE_REFRESH_LEAD_MS = 60_000;

export interface AuthSessionState {
  isBrowser(): boolean;
  getSession(): AuthSession | null;
  setSession(session: AuthSession): void;
  clearSession(): void;
  toSession(result: AuthResult, rememberMe: boolean): AuthSession;
}

@Injectable({ providedIn: 'root' })
export class AuthTokenService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(AuthStorage);
  private readonly networkStatus = inject(NetworkStatusService);

  private refreshInFlight$: Observable<AuthSession | null> | null = null;
  private bootstrapInFlight$: Observable<BootstrapSessionStatus> | null = null;
  private proactiveRefreshTimerId: ReturnType<typeof setTimeout> | null = null;

  bootstrapSessionWithStatus(state: AuthSessionState): Observable<BootstrapSessionStatus> {
    if (!state.isBrowser()) {
      return of('READY');
    }

    if (this.bootstrapInFlight$) {
      return this.bootstrapInFlight$;
    }

    const session = state.getSession();
    if (!session) {
      return of('UNAUTHENTICATED');
    }

    if (!this.isExpired(session.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)) {
      return of('READY');
    }

    if (this.isExpired(session.refreshTokenExpiresAt)) {
      state.clearSession();
      return of('UNAUTHENTICATED');
    }

    const refresh$ = this.refreshAccessToken(state, { preserveSessionOnError: true }).pipe(
      switchMap(async (refreshedSession): Promise<BootstrapSessionStatus> => {
        if (refreshedSession) {
          return 'READY';
        }

        if (await this.shouldPreserveSessionAfterRefreshFailure()) {
          return 'API_UNREACHABLE';
        }

        state.clearSession();
        return 'REFRESH_FAILED';
      }),
      catchError((error) => this.resolveBootstrapRefreshFailure(state, error)),
      finalize(() => {
        this.bootstrapInFlight$ = null;
      }),
      shareReplay(1)
    );

    this.bootstrapInFlight$ = refresh$;

    return refresh$;
  }

  refreshAccessToken(
    state: AuthSessionState,
    options?: { readonly preserveSessionOnError?: boolean }
  ): Observable<AuthSession | null> {
    if (!state.isBrowser()) {
      return of(null);
    }

    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    const session = state.getSession();
    if (!session || this.isExpired(session.refreshTokenExpiresAt)) {
      state.clearSession();
      return of(null);
    }

    const payload: RefreshTokenRequest = { refreshToken: session.refreshToken };

    this.refreshInFlight$ = this.http.post<AuthResult>(AUTH_ENDPOINTS.refreshToken, payload).pipe(
      map((result) => state.toSession(result, session.rememberMe)),
      tap((refreshedSession) => state.setSession(refreshedSession)),
      catchError((error) => {
        // Another tab may have already rotated the token and saved a fresh session
        // to localStorage. Re-read storage before clearing to avoid spurious logout
        // on multi-tab refresh races.
        const fresherSession = this.storage.loadSession();
        if (
          fresherSession
          && fresherSession.accessToken !== session.accessToken
          && !this.isExpired(fresherSession.accessTokenExpiresAt, CLOCK_SKEW_BUFFER_MS)
        ) {
          state.setSession(fresherSession);
          return of(fresherSession);
        }
        if (!options?.preserveSessionOnError) {
          state.clearSession();
        }
        return throwError(() => error);
      }),
      finalize(() => {
        this.refreshInFlight$ = null;
      }),
      shareReplay(1)
    );

    return this.refreshInFlight$;
  }

  revokeToken(refreshToken: string): Observable<void> {
    return this.http.post<void>(AUTH_ENDPOINTS.revokeToken, { refreshToken });
  }

  scheduleProactiveRefresh(state: AuthSessionState, session: AuthSession | null): void {
    this.cancelProactiveRefresh();
    if (!state.isBrowser() || !session) return;

    const expiresAt = Date.parse(session.accessTokenExpiresAt);
    if (Number.isNaN(expiresAt)) return;

    const delay = expiresAt - PROACTIVE_REFRESH_LEAD_MS - Date.now();
    if (delay <= 0) return;

    this.proactiveRefreshTimerId = setTimeout(() => {
      this.proactiveRefreshTimerId = null;
      this.refreshAccessToken(state, { preserveSessionOnError: true }).subscribe({
        error: (error: unknown) => {
          void this.handleProactiveRefreshFailure(state, error);
        },
      });
    }, delay);
  }

  cancelProactiveRefresh(): void {
    if (this.proactiveRefreshTimerId !== null) {
      clearTimeout(this.proactiveRefreshTimerId);
      this.proactiveRefreshTimerId = null;
    }
  }

  isExpired(timestamp: string, bufferMs = 0): boolean {
    const expirationTime = Date.parse(timestamp);

    if (Number.isNaN(expirationTime)) {
      return true;
    }

    return expirationTime - bufferMs <= Date.now();
  }

  private async handleProactiveRefreshFailure(state: AuthSessionState, error: unknown): Promise<void> {
    if (!(await this.shouldPreserveSessionAfterRefreshFailure(error))) {
      state.clearSession();
    }
  }

  private async resolveBootstrapRefreshFailure(state: AuthSessionState, error: unknown): Promise<BootstrapSessionStatus> {
    if (await this.shouldPreserveSessionAfterRefreshFailure(error)) {
      return 'API_UNREACHABLE';
    }

    state.clearSession();
    return 'REFRESH_FAILED';
  }

  private async shouldPreserveSessionAfterRefreshFailure(error?: unknown): Promise<boolean> {
    if (error !== undefined && !this.isNetworkRefreshFailure(error)) {
      return false;
    }

    await this.networkStatus.checkConnectivity();
    return !this.networkStatus.canReachApi();
  }

  private isNetworkRefreshFailure(error: unknown): boolean {
    return error instanceof HttpErrorResponse && error.status === 0;
  }
}
