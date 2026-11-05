import { Injectable, InjectionToken, inject } from '@angular/core';

import { isLegacyProtectedCacheName } from './protected-cache-names';
import { APP_WORKER_REGISTRATION, APP_WORKER_SCRIPT, isAppWorkerScript } from './service-worker-registration';

/**
 * - `safe`: no legacy cache holds a protected answer, and no worker that could store one controls
 *   this page.
 * - `reloadRequired`: this page is still controlled by a worker registered before this build and
 *   the app worker did not take over in time (offline at upgrade, say). Requests from this page
 *   still bypass that worker; a reload finishes the update.
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

/** How long a page under an older worker waits for the app worker to take control of it. */
export const CONTROLLER_HANDOVER_TIMEOUT_MS = 15_000;

/**
 * Moves a browser off the cache rules of earlier builds.
 *
 * Earlier builds let Angular's worker store `/api` answers by URL. This removes what they stored
 * and, when an older worker still controls the page, registers the app worker straight away rather
 * than when the app is next stable, so tabs still running an older build are covered sooner.
 * Only the caches named by `isLegacyProtectedCacheName` are touched: app assets, other caches and
 * every IndexedDB database stay as they are. Each step is safe to repeat.
 * See docs/frontend-architecture.md § Protected API Cache Isolation.
 */
@Injectable({ providedIn: 'root' })
export class ProtectedCacheMigrationService {
  private readonly environment = inject(PROTECTED_CACHE_ENVIRONMENT);

  async ensureSafeVersion(): Promise<CacheSafetyState> {
    if (!this.environment.caches) {
      // No CacheStorage means no worker either: nothing could have been stored.
      return 'safe';
    }

    const purged = await this.purgeLegacyCaches();
    if (!this.isControlledByOlderWorker()) {
      return purged ? 'safe' : 'blocked';
    }

    if (!(await this.handOverToAppWorker())) {
      return 'reloadRequired';
    }

    // Purged again: the older worker kept serving tabs on older builds until the hand-over.
    return (await this.purgeLegacyCaches()) ? 'safe' : 'blocked';
  }

  /**
   * Empties and removes every legacy protected cache. True when none holds an entry afterwards. An
   * older app version still open in another tab may recreate such a cache empty, which is fine.
   */
  async purgeLegacyCaches(): Promise<boolean> {
    const storage = this.environment.caches;
    if (!storage) {
      return true;
    }

    try {
      for (const name of (await storage.keys()).filter(isLegacyProtectedCacheName)) {
        // Entries first: a running older worker may hold a handle to this cache, and deleting the
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
    } catch {
      return false;
    }
  }

  private isControlledByOlderWorker(): boolean {
    const controller = this.environment.serviceWorker?.controller;
    return !!controller && !isAppWorkerScript(controller.scriptURL);
  }

  /** Registers the app worker and waits for it to claim this page, which Angular's worker does on activation. */
  private handOverToAppWorker(): Promise<boolean> {
    const container = this.environment.serviceWorker;
    if (!container) {
      return Promise.resolve(false);
    }

    return new Promise<boolean>((resolve) => {
      const finish = (handedOver: boolean): void => {
        clearTimeout(timer);
        container.removeEventListener('controllerchange', onControllerChange);
        resolve(handedOver);
      };
      const onControllerChange = (): void => {
        if (container.controller && isAppWorkerScript(container.controller.scriptURL)) {
          finish(true);
        }
      };
      const timer = setTimeout(() => finish(false), CONTROLLER_HANDOVER_TIMEOUT_MS);

      container.addEventListener('controllerchange', onControllerChange);
      container.register(APP_WORKER_SCRIPT, APP_WORKER_REGISTRATION).then(onControllerChange, () => finish(false));
    });
  }
}
