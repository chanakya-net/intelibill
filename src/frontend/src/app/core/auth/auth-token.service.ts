import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';

import { catchError, finalize, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';

import { AUTH_ENDPOINTS } from './auth.constants';
import { AuthResult, AuthSession, BootstrapSessionStatus, RefreshTokenRequest } from './auth.models';
import { AuthStorage } from './auth.storage';
import { RefreshCoordinator } from './refresh-coordinator.service';
import { NetworkStatusService } from '../services/network-status.service';

const CLOCK_SKEW_BUFFER_MS = 30_000;
const PROACTIVE_REFRESH_LEAD_MS = 60_000;

export interface AuthSessionState {
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
  private readonly coordinator = inject(RefreshCoordinator);

  private refreshInFlight$: Observable<AuthSession | null> | null = null;
  private bootstrapInFlight$: Observable<BootstrapSessionStatus> | null = null;
  private proactiveRefreshTimerId: ReturnType<typeof setTimeout> | null = null;

  bootstrapSessionWithStatus(state: AuthSessionState): Observable<BootstrapSessionStatus> {
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

  /**
   * Renews the session's access token. Refresh credentials are single-use and the server ends a
   * session whose spent credential is presented again, so renewals of one session are serialized
   * across tabs, and a tab that waited adopts the successor another tab stored instead of
   * presenting the credential it replaced.
   */
  refreshAccessToken(
    state: AuthSessionState,
    options?: { readonly preserveSessionOnError?: boolean }
  ): Observable<AuthSession | null> {
    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    const session = state.getSession();
    if (!session || this.isExpired(session.refreshTokenExpiresAt)) {
      state.clearSession();
      return of(null);
    }

    this.refreshInFlight$ = this.coordinator
      .runExclusive(
        { userId: session.user.id, sessionId: session.sessionId ?? null },
        () => this.rotate(state, session, options)
      )
      .pipe(
        finalize(() => {
          this.refreshInFlight$ = null;
        }),
        shareReplay(1)
      );

    return this.refreshInFlight$;
  }

  /** Runs while this tab holds the refresh lock for the session it was asked to renew. */
  private rotate(
    state: AuthSessionState,
    presented: AuthSession,
    options?: { readonly preserveSessionOnError?: boolean }
  ): Observable<AuthSession | null> {
    const current = state.getSession();
    if (!current) {
      // Signed out (here or in another tab) while waiting: there is nothing left to renew.
      return of(null);
    }

    const successor = this.findSuccessor(presented, [current, this.storage.loadSession()]);
    if (successor) {
      if (successor !== current) {
        state.setSession(successor);
      }
      return of(successor);
    }

    const payload: RefreshTokenRequest = {
      refreshToken: presented.refreshToken,
      activeShopId: presented.activeShopId ?? null,
    };

    return this.http.post<AuthResult>(AUTH_ENDPOINTS.refreshToken, payload).pipe(
      map((result) => state.toSession(result, presented.rememberMe)),
      tap((refreshedSession) => state.setSession(refreshedSession)),
      catchError((error: unknown) => {
        // The server's refusal is final. Nothing stored is adopted after it: a refused credential
        // for this device means the session has ended, and any successor stored for it ended
        // with it. Only the caller decides whether a network failure keeps the session offline.
        if (!options?.preserveSessionOnError) {
          state.clearSession();
        }
        return throwError(() => error);
      })
    );
  }

  /**
   * A renewal another tab already completed for the same device: same user, same session, a
   * different refresh credential that expires no earlier. A different user's session is never a
   * successor, however recent.
   */
  private findSuccessor(
    presented: AuthSession,
    candidates: readonly (AuthSession | null)[]
  ): AuthSession | null {
    return (
      candidates.find(
        (candidate): candidate is AuthSession =>
          !!candidate
          && candidate.user.id === presented.user.id
          && (candidate.sessionId ?? null) === (presented.sessionId ?? null)
          && candidate.refreshToken !== presented.refreshToken
          && !this.isExpired(candidate.refreshTokenExpiresAt)
          && Date.parse(candidate.refreshTokenExpiresAt) >= Date.parse(presented.refreshTokenExpiresAt)
      ) ?? null
    );
  }

  revokeToken(refreshToken: string): Observable<void> {
    return this.http.post<void>(AUTH_ENDPOINTS.revokeToken, { refreshToken });
  }

  scheduleProactiveRefresh(state: AuthSessionState, session: AuthSession | null): void {
    this.cancelProactiveRefresh();
    if (!session) return;

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
