import { Injectable, InjectionToken, inject } from '@angular/core';
import { SwRegistrationOptions } from '@angular/service-worker';

import { isLegacyProtectedCacheName } from './protected-cache-names';
import { PROTECTED_WORKER_PROTOCOL, PROTECTED_WORKER_QUERY } from './service-worker-registration';

/**
 * - `safe`: the worker serving this origin, if any, keeps protected traffic away from every cache,
 *   and no legacy cache holds an answer.
 * - `reloadRequired`: an earlier worker still serves this origin and could not be replaced now
 *   (offline, say). Requests from this page still bypass it; a reload tries again.
 * - `blocked`: the browser refused the cleanup, so earlier answers may still be stored. A reload
 *   tries again. Local business data (queued invoices, drafts) is never touched either way.
 */
export type CacheSafetyState = 'safe' | 'reloadRequired' | 'blocked';

export interface ProtectedCacheEnvironment {
  readonly caches: CacheStorage | null;
  readonly serviceWorker: ServiceWorkerContainer | null;
}

export const PROTECTED_CACHE_ENVIRONMENT = new InjectionToken<ProtectedCacheEnvironment>('PROTECTED_CACHE_ENVIRONMENT', {
  providedIn: 'root',
  factory: () => ({
    caches: typeof caches === 'undefined' ? null : caches,
    serviceWorker: typeof navigator !== 'undefined' && 'serviceWorker' in navigator ? navigator.serviceWorker : null,
  }),
});

/** How long a worker has to answer the isolation query; an earlier worker never does. */
export const WORKER_QUERY_TIMEOUT_MS = 2_000;
/** How long the browser has to fetch and compare the registered script. */
export const WORKER_UPDATE_TIMEOUT_MS = 15_000;
/**
 * How long a replacement worker has to activate. Activation waits for the outgoing worker's events to
 * settle, and Angular's worker holds each fetch event open for its idle tasks for up to 30 s
 * (`MAX_IDLE_DELAY`).
 */
export const WORKER_HANDOVER_TIMEOUT_MS = 35_000;

type WorkerCheck = 'none' | 'protected' | 'replaced' | 'stale';

/**
 * Moves a browser off the cache rules of earlier builds.
 *
 * Earlier builds let Angular's worker store `/api` answers by URL and replay them. Only replacing that
 * worker stops the replay: after a page deletes a cache, a running earlier worker can still store and
 * replay new answers through the handle it holds, which no page can reach any more. So this asks the
 * active worker whether it is Intelibill's (which keeps protected traffic out of every cache), and if
 * not asks the browser to fetch the current script now rather than at its next update check. It also
 * removes the answers earlier workers stored, so none stays on disk.
 * Only the caches named by `isLegacyProtectedCacheName` are touched: app assets, other caches and
 * every IndexedDB database stay as they are. Each step is safe to repeat.
 * See docs/frontend-architecture.md § Protected API Cache Isolation.
 */
@Injectable({ providedIn: 'root' })
export class ProtectedCacheMigrationService {
  private readonly environment = inject(PROTECTED_CACHE_ENVIRONMENT);
  /**
   * Development builds register no worker; one left over from a production build is not theirs to
   * manage. Read as Angular reads it: provided, and `enabled` not set to false.
   */
  private readonly workerOptions = inject(SwRegistrationOptions, { optional: true });
  private readonly workerEnabled = this.workerOptions !== null && this.workerOptions.enabled !== false;

  async ensureSafeVersion(): Promise<CacheSafetyState> {
    const purged = await this.purgeLegacyCaches();

    switch (await this.checkWorker()) {
      case 'stale':
        return 'reloadRequired';
      case 'replaced':
        // Purged again: the earlier worker kept storing answers for older tabs until it was replaced.
        return (await this.purgeLegacyCaches()) ? 'safe' : 'blocked';
      default:
        return purged ? 'safe' : 'blocked';
    }
  }

  /**
   * Empties and removes every legacy protected cache. True when none holds an entry afterwards (an
   * older app version still open in another tab may recreate one empty, which is fine), or when the
   * browser denies this origin CacheStorage altogether: then no worker can read it either.
   */
  async purgeLegacyCaches(): Promise<boolean> {
    const storage = this.environment.caches;
    if (!storage) {
      return true;
    }

    try {
      for (const name of (await storage.keys()).filter(isLegacyProtectedCacheName)) {
        // Entries first: a running earlier worker may hold a handle to this cache, and deleting the
        // name alone would leave that handle's entries readable.
        const cache = await storage.open(name);
        for (const request of await cache.keys()) {
          await cache.delete(request);
        }
        await storage.delete(name);
      }

      for (const name of (await storage.keys()).filter(isLegacyProtectedCacheName)) {
        if ((await (await storage.open(name)).keys()).length > 0) {
          return false;
        }
      }
      return true;
    } catch (error) {
      return error instanceof DOMException && error.name === 'SecurityError';
    }
  }

  /** Calls `listener` whenever another worker takes control of this page. Returns the unsubscribe. */
  watchWorkerChanges(listener: () => void): () => void {
    const container = this.environment.serviceWorker;
    if (!this.workerEnabled || !container) {
      return () => undefined;
    }

    container.addEventListener('controllerchange', listener);
    return () => container.removeEventListener('controllerchange', listener);
  }

  private async checkWorker(): Promise<WorkerCheck> {
    const container = this.environment.serviceWorker;
    if (!this.workerEnabled || !container) {
      return 'none';
    }

    let registration: ServiceWorkerRegistration | undefined;
    try {
      registration = await container.getRegistration();
    } catch {
      return 'none';
    }

    const active = registration?.active;
    if (!registration || !active) {
      return 'none';
    }
    if (await this.isProtectedWorker(active)) {
      return 'protected';
    }

    if (!(await this.fetchCurrentScript(registration))) {
      return 'stale';
    }

    const incoming = registration.installing ?? registration.waiting;
    if (incoming && !(await this.activation(incoming))) {
      return 'stale';
    }

    const current = registration.active;
    return current && (await this.isProtectedWorker(current)) ? 'replaced' : 'stale';
  }

  /** True once the browser has fetched and compared the registered script; false if that fails or hangs. */
  private fetchCurrentScript(registration: ServiceWorkerRegistration): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      const timer = setTimeout(() => resolve(false), WORKER_UPDATE_TIMEOUT_MS);
      registration.update().then(
        () => {
          clearTimeout(timer);
          resolve(true);
        },
        () => {
          clearTimeout(timer);
          resolve(false);
        },
      );
    });
  }

  private isProtectedWorker(worker: ServiceWorker): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      const channel = new MessageChannel();
      const finish = (isProtected: boolean): void => {
        clearTimeout(timer);
        channel.port1.close();
        resolve(isProtected);
      };
      const timer = setTimeout(() => finish(false), WORKER_QUERY_TIMEOUT_MS);

      channel.port1.onmessage = (event: MessageEvent<{ protectedApiIsolation?: number } | null>) =>
        finish(event.data?.protectedApiIsolation === PROTECTED_WORKER_PROTOCOL);
      try {
        worker.postMessage({ action: PROTECTED_WORKER_QUERY }, [channel.port2]);
      } catch {
        finish(false);
      }
    });
  }

  /** Resolves true once `worker` is activated (Angular's worker skips waiting), false if it fails or stalls. */
  private activation(worker: ServiceWorker): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      const settle = (): boolean => {
        if (worker.state === 'activated' || worker.state === 'redundant') {
          finish(worker.state === 'activated');
          return true;
        }
        return false;
      };
      const finish = (activated: boolean): void => {
        clearTimeout(timer);
        worker.removeEventListener('statechange', settle);
        resolve(activated);
      };
      const timer = setTimeout(() => finish(false), WORKER_HANDOVER_TIMEOUT_MS);

      if (!settle()) {
        worker.addEventListener('statechange', settle);
      }
    });
  }
}
