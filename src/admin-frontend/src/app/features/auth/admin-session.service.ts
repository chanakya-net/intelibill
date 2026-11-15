import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import type { SessionPort, SessionState } from '@intelibill/api';
import { type Observable, catchError, map, of } from 'rxjs';
import { AdminRefreshCoordinator, explicitSessionRecovery } from '../../core/auth/admin-refresh-coordinator';

interface SessionSnapshot {
  capabilities: readonly string[];
  sessionId: string;
  accessExpiresAt: string;
}

export type SessionCheck = 'active' | 'ended' | 'uncertain';
/** Why protected content is hidden: access lapsed locally, the server ended it, or it is unconfirmed. */
type SignOutNotice = 'signing-out' | 'unconfirmed' | 'signed-out';
export type SessionNotice = 'none' | 'expired' | 'ended' | 'uncertain';

export class AdminSessionChangedError extends HttpErrorResponse {
  constructor(url: string) {
    super({ status: 401, url });
  }
}

@Injectable({ providedIn: 'root' })
export class AdminSessionService implements SessionPort {
  private readonly http = inject(HttpClient);
  private readonly coordinator = inject(AdminRefreshCoordinator);
  private readonly endNotice = signal<SignOutNotice | null>(null);
  private readonly currentId = signal<string | null>(null);
  private confirmedGeneration: string | undefined;
  private endGeneration: string | undefined;
  private ending?: Promise<void>;
  private readonly sessionState = signal<SessionState>('unauthenticated');
  private readonly grants = signal<readonly string[]>([]);
  private readonly reason = signal<SessionNotice>('none');
  private endedVersion = 0;
  private expiry?: ReturnType<typeof setTimeout>;

  readonly state = this.sessionState.asReadonly();
  readonly capabilities = this.grants.asReadonly();
  readonly notice = this.reason.asReadonly();
  readonly signOutNotice = this.endNotice.asReadonly();
  readonly sessionId = this.currentId.asReadonly();

  get endVersion(): number {
    return this.endedVersion;
  }

  constructor() {
    const destroy = inject(DestroyRef);
    this.syncSignOut();
    const stopObserving = this.coordinator.observeSessionChange((reason) => {
      if (reason === 'sign-out') this.syncSignOut();
      else if (!this.syncSignOut()) this.settle(reason);
    });
    destroy.onDestroy(() => {
      stopObserving();
      clearTimeout(this.expiry);
    });
  }

  /** Reads the live session; an expired access token is renewed on the way by the session interceptor. */
  check(explicit = false): Observable<SessionCheck> {
    if (this.syncSignOut()) return of('ended');
    const context = new HttpContext().set(explicitSessionRecovery, explicit);
    return this.http.get<SessionSnapshot>('/api/admin/auth/session', { context }).pipe(
      map((snapshot) => (this.accept(snapshot) ? 'active' : this.settle('ended'))),
      catchError((error: unknown) => of(error instanceof AdminSessionChangedError
        ? this.currentCheck()
        : this.settle(isUncertain(error) ? 'uncertain' : 'ended'))),
    );
  }

  signOut(): Promise<void> {
    return this.endSession(this.confirmedGeneration);
  }

  retrySignOut(): Promise<void> {
    if (this.endNotice() !== 'unconfirmed') return Promise.resolve();
    return this.endSession(this.endGeneration);
  }

  syncSignOut(): boolean {
    const marker = this.coordinator.signOutMarker();
    if (marker) {
      const localRequestIsPending = this.endNotice() === 'signing-out' && marker.generation === this.endGeneration;
      this.endGeneration = marker.generation;
      if (marker.confirmed) this.setSignOutNotice('signed-out');
      else if (!localRequestIsPending) this.setSignOutNotice('unconfirmed');
    } else if (this.endNotice() && this.generationHasChanged(this.endGeneration)) {
      this.endNotice.set(null);
    }
    return this.endNotice() !== null;
  }

  private endSession(generation: string | undefined): Promise<void> {
    if (this.ending) return this.ending;
    this.endGeneration = generation;
    this.setSignOutNotice('signing-out');
    const operation = this.coordinator.signOut(generation).then((outcome) => {
      if (this.generationHasChanged(generation)) {
        this.syncSignOut();
        return;
      }
      this.setSignOutNotice(outcome === 'confirmed' || this.coordinator.signOutMarker()?.confirmed
        ? 'signed-out' : 'unconfirmed');
    }).finally(() => {
      this.ending = undefined;
    });
    this.ending = operation;
    return operation;
  }

  private generationHasChanged(generation: string | undefined): boolean {
    const current = this.coordinator.generation();
    return typeof current === 'string' && current !== generation;
  }

  private setSignOutNotice(notice: SignOutNotice) {
    if (this.endNotice() !== notice) this.settle('ended');
    this.endNotice.set(notice);
  }

  /** Hides every grant; only a later successful check shows them again. */
  settle<T extends Exclude<SessionNotice, 'none'>>(notice: T): T {
    if (notice === 'ended') this.endedVersion++;
    this.clear('unauthenticated');
    this.reason.set(notice);
    return notice;
  }

  private currentCheck(): SessionCheck {
    if (this.state() === 'authenticated') return 'active';
    return this.notice() === 'ended' ? 'ended' : 'uncertain';
  }

  private accept(snapshot: SessionSnapshot) {
    const expiresAt = Date.parse(snapshot.accessExpiresAt);
    if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) return false;
    this.clear('authenticated');
    const generation = this.coordinator.generation();
    this.confirmedGeneration = generation === null ? undefined : generation;
    this.currentId.set(snapshot.sessionId ?? null);
    this.reason.set('none');
    this.grants.set(snapshot.capabilities);
    // No background renewal: renewing without user activity would defeat the idle limit.
    this.expiry = setTimeout(() => this.settle('expired'), expiresAt - Date.now());
    return true;
  }

  private clear(state: SessionState) {
    clearTimeout(this.expiry);
    this.grants.set([]);
    this.currentId.set(null);
    this.sessionState.set(state);
  }
}

function isUncertain(error: unknown) {
  if (!(error instanceof HttpErrorResponse)) return true;
  return error.status === 0 || error.status === 408 || error.status === 429 || error.status >= 500;
}
