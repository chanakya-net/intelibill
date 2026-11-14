import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import type { SessionPort, SessionState } from '@intelibill/api';
import { catchError, map, of } from 'rxjs';

interface SessionSnapshot {
  capabilities: readonly string[];
  accessExpiresAt: string;
}

@Injectable({ providedIn: 'root' })
export class AdminSessionService implements SessionPort {
  private readonly http = inject(HttpClient);
  private readonly sessionState = signal<SessionState>('unauthenticated');
  private readonly grants = signal<readonly string[]>([]);
  private expiry?: ReturnType<typeof setTimeout>;

  readonly state = this.sessionState.asReadonly();
  readonly capabilities = this.grants.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.expiry));
  }

  check() {
    return this.http.get<SessionSnapshot>('/api/admin/auth/session').pipe(
      map((snapshot) => {
        const expiresAt = Date.parse(snapshot.accessExpiresAt);
        if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) {
          this.clear('unauthenticated');
          return false;
        }
        this.clear('authenticated');
        this.grants.set(snapshot.capabilities);
        this.expiry = setTimeout(() => this.clear('unauthenticated'), expiresAt - Date.now());
        return true;
      }),
      catchError(() => {
        this.clear('unauthenticated');
        return of(false);
      }),
    );
  }

  private clear(state: SessionState) {
    clearTimeout(this.expiry);
    this.grants.set([]);
    this.sessionState.set(state);
  }
}
