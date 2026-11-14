import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import type { SessionPort, SessionState } from '@intelibill/api';
import { type Observable, catchError, map, of } from 'rxjs';
import { AdminRefreshCoordinator, explicitSessionRecovery } from '../../core/auth/admin-refresh-coordinator';

interface SessionSnapshot {
  capabilities: readonly string[];
  accessExpiresAt: string;
}

export type SessionCheck = 'active' | 'ended' | 'uncertain';
/** Why protected content is hidden: access lapsed locally, the server ended it, or it is unconfirmed. */
export type SessionNotice = 'none' | 'expired' | 'ended' | 'uncertain';

@Injectable({ providedIn: 'root' })
export class AdminSessionService implements SessionPort {
  private readonly http = inject(HttpClient);
  private readonly sessionState = signal<SessionState>('unauthenticated');
  private readonly grants = signal<readonly string[]>([]);
  private readonly reason = signal<SessionNotice>('none');
  private expiry?: ReturnType<typeof setTimeout>;

  readonly state = this.sessionState.asReadonly();
  readonly capabilities = this.grants.asReadonly();
  readonly notice = this.reason.asReadonly();

  constructor() {
    const destroy = inject(DestroyRef);
    const stopObserving = inject(AdminRefreshCoordinator).observeSessionChange((reason) => this.settle(reason));
    destroy.onDestroy(() => {
      stopObserving();
      clearTimeout(this.expiry);
    });
  }

  /** Reads the live session; an expired access token is renewed on the way by the session interceptor. */
  check(explicit = false): Observable<SessionCheck> {
    const context = new HttpContext().set(explicitSessionRecovery, explicit);
    return this.http.get<SessionSnapshot>('/api/admin/auth/session', { context }).pipe(
      map((snapshot) => (this.accept(snapshot) ? 'active' : this.settle('ended'))),
      catchError((error: unknown) => of(this.settle(isUncertain(error) ? 'uncertain' : 'ended'))),
    );
  }

  /** Hides every grant; only a later successful check shows them again. */
  settle<T extends Exclude<SessionNotice, 'none'>>(notice: T): T {
    this.clear('unauthenticated');
    this.reason.set(notice);
    return notice;
  }

  private accept(snapshot: SessionSnapshot) {
    const expiresAt = Date.parse(snapshot.accessExpiresAt);
    if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) return false;
    this.clear('authenticated');
    this.reason.set('none');
    this.grants.set(snapshot.capabilities);
    // No background renewal: renewing without user activity would defeat the idle limit.
    this.expiry = setTimeout(() => this.settle('expired'), expiresAt - Date.now());
    return true;
  }

  private clear(state: SessionState) {
    clearTimeout(this.expiry);
    this.grants.set([]);
    this.sessionState.set(state);
  }
}

function isUncertain(error: unknown) {
  if (!(error instanceof HttpErrorResponse)) return true;
  return error.status === 0 || error.status === 408 || error.status === 429 || error.status >= 500;
}
