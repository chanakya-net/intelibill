import { Injectable } from '@angular/core';

/**
 * The offline pack (the app's own files cached for offline use) belongs to the device, not to a
 * shop: every shop enabled on this browser shares it. See docs/frontend-architecture.md § Offline Pack.
 */
export interface OfflinePackStatus {
  /** `ready`: every file of the full manifest was found in the worker's caches; `incomplete`: not. */
  readonly state: 'ready' | 'incomplete';
  /** The build the pack was last verified for (`appData.buildId` of `ngsw.json`). */
  readonly buildId: string | null;
  /** The manifest hash Angular's worker files that version under. */
  readonly manifestHash: string | null;
  /** Whether the browser agreed to keep this site's storage (`navigator.storage.persist`). */
  readonly persisted: boolean | null;
  /** When the pack was last checked against the deployed manifest. */
  readonly lastCheckedAt: string | null;
  /** When a different build was first verified complete on this device. */
  readonly lastUpdatedAt: string | null;
}

const OFFLINE_PACK_KEY = 'intelibill.offlineSales.offlinePack.v1';

/** Device-level record of the offline pack, beside the per-shop offline device settings. */
@Injectable({ providedIn: 'root' })
export class OfflinePackStatusStorage {
  loadPackStatus(): OfflinePackStatus | null {
    if (typeof localStorage === 'undefined') {
      return null;
    }

    let raw: Partial<OfflinePackStatus> | null;
    try {
      raw = JSON.parse(localStorage.getItem(OFFLINE_PACK_KEY) ?? 'null') as Partial<OfflinePackStatus> | null;
    } catch {
      return null;
    }
    if (!raw || (raw.state !== 'ready' && raw.state !== 'incomplete')) {
      return null;
    }

    return {
      state: raw.state,
      buildId: typeof raw.buildId === 'string' ? raw.buildId : null,
      manifestHash: typeof raw.manifestHash === 'string' ? raw.manifestHash : null,
      persisted: typeof raw.persisted === 'boolean' ? raw.persisted : null,
      lastCheckedAt: typeof raw.lastCheckedAt === 'string' ? raw.lastCheckedAt : null,
      lastUpdatedAt: typeof raw.lastUpdatedAt === 'string' ? raw.lastUpdatedAt : null,
    };
  }

  /** Stores the pack status; null forgets it (offline is off on this device). */
  savePackStatus(status: OfflinePackStatus | null): void {
    if (typeof localStorage === 'undefined') {
      return;
    }

    try {
      if (status) {
        localStorage.setItem(OFFLINE_PACK_KEY, JSON.stringify(status));
      } else {
        localStorage.removeItem(OFFLINE_PACK_KEY);
      }
    } catch {
      // Storage full or denied: the status is advisory and is worked out again on the next check.
    }
  }
}
