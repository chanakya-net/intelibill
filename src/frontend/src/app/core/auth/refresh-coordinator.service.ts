import { Injectable, InjectionToken, inject } from '@angular/core';

import { finalize, Observable, Subscription } from 'rxjs';

/** The slice of the Web Locks API the coordinator uses; `navigator.locks` satisfies it. */
export interface RefreshLockManager {
  request(
    name: string,
    options: { readonly mode: 'exclusive' },
    callback: () => Promise<void>,
  ): Promise<void>;
}

/** One device session: refreshes are serialized per user and session, never across them. */
export interface RefreshLockScope {
  readonly userId: string;
  readonly sessionId: string | null;
}

export const REFRESH_LOCK_MANAGER = new InjectionToken<RefreshLockManager | null>(
  'REFRESH_LOCK_MANAGER',
  {
    providedIn: 'root',
    factory: () =>
      typeof navigator !== 'undefined' && navigator.locks ? navigator.locks : null,
  },
);

/** Shared storage for the fallback lease when the Web Locks API is missing. */
export const REFRESH_LEASE_STORAGE = new InjectionToken<Storage | null>('REFRESH_LEASE_STORAGE', {
  providedIn: 'root',
  factory: () => {
    try {
      return typeof localStorage === 'undefined' ? null : localStorage;
    } catch {
      return null;
    }
  },
});

const LOCK_PREFIX = 'intelibill.auth.refresh';
const LEASE_PREFIX = 'intelibill.auth.refresh-lease';

/** Longer than a refresh round trip, so a live holder is not overtaken; short enough that a closed tab's lease does not strand the others. */
export const REFRESH_LEASE_TTL_MS = 15_000;
/** How long a tab waits for the fallback lease before refreshing anyway. */
export const REFRESH_LEASE_WAIT_LIMIT_MS = 10_000;
export const REFRESH_LEASE_POLL_MS = 100;

interface Lease {
  readonly owner: string;
  readonly expiresAt: number;
}

/**
 * Lets one browser tab at a time renew a given session. Refresh credentials are single-use: the
 * server treats a credential presented after it was already exchanged as a replay and ends the
 * session, so two tabs renewing together would sign the user out of both. The tab that waited
 * re-reads the session once it holds the lock and adopts what the first tab stored.
 *
 * Uses the Web Locks API. Where that is missing, a localStorage lease stands in: it is best-effort
 * and bounded, and the server's answer remains the final word either way.
 */
@Injectable({ providedIn: 'root' })
export class RefreshCoordinator {
  private readonly locks = inject(REFRESH_LOCK_MANAGER);
  private readonly leaseStorage = inject(REFRESH_LEASE_STORAGE);
  private readonly owner = createOwnerId();

  /**
   * Subscribes to `work` only while holding the lock for `scope`, and holds it until `work`
   * completes, errors or is unsubscribed.
   */
  runExclusive<T>(scope: RefreshLockScope, work: () => Observable<T>): Observable<T> {
    const name = `${scope.userId}:${scope.sessionId ?? 'unknown'}`;

    return new Observable<T>((subscriber) => {
      let release: () => void = () => undefined;
      const held = new Promise<void>((resolve) => {
        release = resolve;
      });
      let closed = false;
      let inner: Subscription | undefined;
      let retry: ReturnType<typeof setTimeout> | undefined;

      const start = (): void => {
        if (closed) {
          release();
          return;
        }

        inner = work()
          .pipe(finalize(() => release()))
          .subscribe(subscriber);
      };

      if (this.locks) {
        this.locks
          .request(`${LOCK_PREFIX}:${name}`, { mode: 'exclusive' }, () => {
            start();
            return held;
          })
          .catch((error: unknown) => subscriber.error(error));
      } else {
        const leaseKey = `${LEASE_PREFIX}:${name}`;
        const deadline = Date.now() + REFRESH_LEASE_WAIT_LIMIT_MS;
        const attempt = (): void => {
          if (closed) {
            return;
          }

          const releaseLease =
            this.tryTakeLease(leaseKey) ?? (Date.now() >= deadline ? noop : null);
          if (!releaseLease) {
            retry = setTimeout(attempt, REFRESH_LEASE_POLL_MS);
            return;
          }

          void held.then(releaseLease);
          start();
        };
        attempt();
      }

      return () => {
        closed = true;
        if (retry !== undefined) {
          clearTimeout(retry);
        }
        inner?.unsubscribe();
        release();
      };
    });
  }

  /**
   * Takes the lease if nobody else holds a live one, and returns how to give it back; null while
   * another tab holds it. localStorage has no compare-and-swap, so the write is read back: if
   * another tab's write landed in between, its lease stands.
   */
  private tryTakeLease(key: string): (() => void) | null {
    const storage = this.leaseStorage;
    if (!storage) {
      return noop;
    }

    try {
      const now = Date.now();
      const current = parseLease(storage.getItem(key));
      if (current && current.owner !== this.owner && current.expiresAt > now) {
        return null;
      }

      const lease: Lease = { owner: this.owner, expiresAt: now + REFRESH_LEASE_TTL_MS };
      storage.setItem(key, JSON.stringify(lease));
      if (parseLease(storage.getItem(key))?.owner !== this.owner) {
        return null;
      }

      return () => {
        try {
          if (parseLease(storage.getItem(key))?.owner === this.owner) {
            storage.removeItem(key);
          }
        } catch {
          // Storage went away; the lease expires on its own.
        }
      };
    } catch {
      // Storage is unusable (private mode, quota): there is nothing to coordinate through.
      return noop;
    }
  }
}

function noop(): void {
  // Nothing to release.
}

function parseLease(raw: string | null): Lease | null {
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as Partial<Lease>;
    return typeof parsed.owner === 'string' && typeof parsed.expiresAt === 'number'
      ? { owner: parsed.owner, expiresAt: parsed.expiresAt }
      : null;
  } catch {
    return null;
  }
}

function createOwnerId(): string {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}
