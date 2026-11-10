import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { SwUpdate, type VersionEvent } from '@angular/service-worker';
import type { Subscription } from 'rxjs';

import { OfflinePackStatus, OfflinePackStatusStorage } from '../storage/offline-pack-status.storage';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import {
  FULL_MANIFEST_PATH,
  OFFLINE_PACK_STATE_CACHE,
  OFFLINE_PACK_STATE_KEY,
  angularManifestHash,
  type OfflinePackFlag,
} from './offline-pack-flag';
import { APP_WORKER_SCRIPT } from './service-worker-registration';

export interface OfflinePackEnvironment {
  readonly caches: CacheStorage | null;
  readonly serviceWorker: ServiceWorkerContainer | null;
  readonly storage: StorageManager | null;
  readonly fetch: (input: string, init?: RequestInit) => Promise<Response>;
  /** The document base the app's files resolve against. */
  readonly baseUrl: () => string;
  readonly now: () => number;
  readonly delay: (ms: number) => Promise<void>;
}

export const OFFLINE_PACK_ENVIRONMENT = new InjectionToken<OfflinePackEnvironment>('OFFLINE_PACK_ENVIRONMENT', {
  providedIn: 'root',
  factory: () => ({
    caches: typeof caches === 'undefined' ? null : caches,
    serviceWorker: typeof navigator !== 'undefined' && 'serviceWorker' in navigator ? navigator.serviceWorker : null,
    storage: typeof navigator !== 'undefined' && navigator.storage ? navigator.storage : null,
    fetch: (input, init) => fetch(input, init),
    baseUrl: () => document.baseURI,
    now: () => Date.now(),
    delay: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  }),
});

/** Free storage the device needs before the pack is downloaded: the pack (~7.5 MB raw) plus room for data. */
export const OFFLINE_PACK_MIN_FREE_BYTES = 25 * 1024 * 1024;
/** How long enabling waits for the worker to download and verify the pack (a slow 3G link needs about a minute). */
export const OFFLINE_PACK_READY_TIMEOUT_MS = 180_000;
/** How often enabling looks at the worker's caches while it downloads. */
export const OFFLINE_PACK_POLL_MS = 1_000;
/** How long an update check may take before it is given up (the worker may not be installed yet). */
export const OFFLINE_PACK_CHECK_TIMEOUT_MS = 60_000;
/** How long enabling waits for a worker it had to register itself to take control of the page. */
export const OFFLINE_PACK_CONTROL_TIMEOUT_MS = 30_000;

export type OfflinePackFailure = 'STORAGE_LOW' | 'INSTALL_FAILED' | 'TIMEOUT' | 'UNREACHABLE';

export type OfflinePackPrepareResult =
  | { readonly ok: true; readonly skipped: true }
  | {
      readonly ok: true;
      readonly skipped: false;
      readonly status: OfflinePackStatus;
      /**
       * The pack is on the device, but this tab runs an earlier build than the one installed (a newer
       * build was deployed): its files not yet loaded are not in the pack until the update is applied.
       */
      readonly reloadRequired: boolean;
    }
  | { readonly ok: false; readonly failure: OfflinePackFailure };

interface FullManifest {
  readonly hash: string;
  readonly buildId: string | null;
  /** Every file of every prefetched group, by the cache Angular's worker files it under. */
  readonly groups: readonly { readonly name: string; readonly urls: readonly string[] }[];
}

export interface OfflinePackVerification {
  readonly complete: boolean;
  readonly missing: readonly string[];
  readonly manifest: FullManifest;
}

interface ManifestJson {
  readonly appData?: { readonly buildId?: unknown };
  readonly assetGroups?: readonly { readonly name?: unknown; readonly installMode?: unknown; readonly urls?: unknown }[];
}

/**
 * The app's own files cached for offline use, downloaded only on a device where offline billing is
 * enabled for some shop.
 *
 * The worker (`service-worker/protected-api-worker.js`) shows Angular's worker the full manifest
 * unless this page has written the flag `off` (see `offline-pack-flag.ts`). Angular then installs a
 * changed manifest only once every prefetched file has downloaded and matched its hash. This
 * service writes the flag and checks the result:
 *
 * - A tab keeps the version it loaded with until it activates another, so after a lite -> full
 *   switch of the same build it activates the new version itself (no reload: the files are the same).
 * - `VERSION_INSTALLATION_FAILED` means the worker may serve new tabs from the network until the
 *   browser restarts it, so it is a failure, never "probably fine".
 * - A worker's very first install is not all-or-nothing (it records the manifest before
 *   prefetching), so `VERSION_READY` is never the proof: every file of the full manifest is looked
 *   up in the worker's caches, and the worker must report a normal state.
 * - Switching back to lite leaves the pack until the worker next starts with no tab on it; the
 *   device stops relying on it at once.
 *
 * Without a service worker (development builds, unsupported browsers) every step is skipped and
 * offline billing is enabled as before this existed: such a page never had an offline pack.
 * See docs/frontend-architecture.md § Offline Pack.
 */
@Injectable({ providedIn: 'root' })
export class OfflinePackService {
  private readonly environment = inject(OFFLINE_PACK_ENVIRONMENT);
  private readonly swUpdate = inject(SwUpdate, { optional: true });
  private readonly settingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly statusStorage = inject(OfflinePackStatusStorage);

  private readonly statusSignal = signal<OfflinePackStatus | null>(this.statusStorage.loadPackStatus());
  private readonly reloadRequiredSignal = signal(false);
  /** The worker has a different build ready than the one this tab runs. */
  private newerBuildReady = false;

  /** The last verified state of the pack on this device, or null when it is not in use. */
  readonly status = this.statusSignal.asReadonly();
  /**
   * Enabling succeeded while this tab runs an earlier build than the installed pack: it needs the
   * update applied ("Update now") before it can rely on the pack offline.
   */
  readonly reloadRequired = this.reloadRequiredSignal.asReadonly();

  constructor() {
    if (this.swUpdate?.isEnabled) {
      // Angular tells a tab once that a build other than its own is ready; enabling may come later.
      this.swUpdate.versionUpdates.subscribe((event) => {
        if (event.type === 'VERSION_READY' && !isSameBuild(event)) {
          this.newerBuildReady = true;
        }
      });
    }
  }

  /** Whether this page runs under a service worker that can hold the pack. */
  isAvailable(): boolean {
    return !!this.swUpdate?.isEnabled && !!this.environment.caches && !!this.environment.serviceWorker;
  }

  /**
   * True when some shop on this device has offline billing enabled; null when the device settings
   * cannot be read. Nothing is switched off on null: a storage error must not drop a working pack.
   */
  deviceNeedsPack(): boolean | null {
    return this.settingsStorage.anyShopEnabled();
  }

  /** Writes the flag the worker reads. False when the browser refused. */
  async writeFlag(flag: OfflinePackFlag): Promise<boolean> {
    const storage = this.environment.caches;
    if (!storage) {
      return false;
    }

    try {
      const cache = await storage.open(OFFLINE_PACK_STATE_CACHE);
      await cache.put(
        new Request(this.resolve(OFFLINE_PACK_STATE_KEY)),
        new Response(flag, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } }),
      );
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Brings the flag in line with the device settings: on when any shop needs the pack, explicit off
   * otherwise, unchanged when the settings cannot be read.
   */
  async reconcile(): Promise<void> {
    if (!this.isAvailable()) {
      return;
    }

    const needed = this.deviceNeedsPack();
    if (needed === null) {
      return;
    }
    await this.writeFlag(needed ? 'on' : 'off');
    if (!needed) {
      this.setStatus(null);
    }
  }

  /**
   * The enable step: makes sure the full pack is on this device before any shop relies on it.
   * The caller releases the flag again (`release`) when this or a later step fails.
   */
  async prepare(): Promise<OfflinePackPrepareResult> {
    if (!this.isAvailable()) {
      // No service worker (development build or unsupported browser): enabling works as it did.
      return { ok: true, skipped: true };
    }

    if (await this.storageIsLow()) {
      return { ok: false, failure: 'STORAGE_LOW' };
    }

    const persisted = await this.requestPersistence();
    if (!(await this.writeFlag('on'))) {
      return { ok: false, failure: 'INSTALL_FAILED' };
    }

    const outcome = await this.awaitFullPack();
    if (!outcome.ok) {
      return outcome;
    }

    const status = this.recordVerified(outcome.manifest, persisted);
    // Without the update this tab keeps loading its own build's files, which the pack does not hold.
    const reloadRequired = this.newerBuildReady;
    this.reloadRequiredSignal.set(reloadRequired);
    return { ok: true, skipped: false, status, reloadRequired };
  }

  /**
   * Called when a shop stops needing the pack (offline disabled, or an enable attempt failed):
   * unless another shop on this device still needs it, the flag goes off and the worker is asked
   * to re-check, which installs the lite manifest. The pack itself is dropped when the worker next
   * starts with no tab on the full version. Kept when the device settings cannot be read.
   */
  async release(): Promise<void> {
    if (!this.isAvailable() || this.deviceNeedsPack() !== false) {
      return;
    }

    await this.writeFlag('off');
    this.setStatus(null);
    this.reloadRequiredSignal.set(false);
    void this.checkForUpdate();
  }

  /**
   * Asks the worker to look for a new manifest. Resolves to whether one was installed, or null when
   * the check could not run (no worker yet, offline, or it took longer than the limit).
   */
  async checkForUpdate(timeoutMs = OFFLINE_PACK_CHECK_TIMEOUT_MS): Promise<boolean | null> {
    if (!this.swUpdate?.isEnabled) {
      return null;
    }

    return this.withTimeout(this.swUpdate.checkForUpdate(), timeoutMs);
  }

  /**
   * Re-verifies the pack against the deployed manifest and records the outcome. Does nothing when
   * the device does not need the pack, and leaves the last state when the manifest cannot be
   * fetched (offline: nothing new to compare against).
   *
   * The deployed manifest is fetched apart from the worker's own check, so a deploy in between finds
   * the new build's files missing although the worker still serves its previous, complete version
   * offline. Missing files of a build the worker has not taken up yet are therefore judged by the
   * version the worker serves (its latest full version): complete there means ready, and the new
   * build is pending, installed by a later check.
   */
  async refreshStatus(): Promise<OfflinePackStatus | null> {
    if (!this.isAvailable() || this.deviceNeedsPack() !== true) {
      return this.statusSignal();
    }

    const verification = await this.verify();
    if (!verification) {
      return this.statusSignal();
    }

    if (verification.complete) {
      return this.recordVerified(verification.manifest, this.statusSignal()?.persisted ?? null);
    }

    const served = await this.servedManifest();
    if (served && served.hash !== verification.manifest.hash && (await this.verify(served))?.complete) {
      return this.recordVerified(served, this.statusSignal()?.persisted ?? null);
    }
    return this.markIncomplete(verification.manifest);
  }

  /** Records that the worker failed to install the pack: until a later check succeeds it may not serve offline. */
  markIncomplete(manifest?: FullManifest): OfflinePackStatus {
    const previous = this.statusSignal();
    const status: OfflinePackStatus = {
      state: 'incomplete',
      buildId: manifest?.buildId ?? previous?.buildId ?? null,
      manifestHash: manifest?.hash ?? previous?.manifestHash ?? null,
      persisted: previous?.persisted ?? null,
      lastCheckedAt: new Date(this.environment.now()).toISOString(),
      lastUpdatedAt: previous?.lastUpdatedAt ?? null,
    };
    this.setStatus(status);
    return status;
  }

  /**
   * Looks up every file of the deployed full manifest in the caches Angular's worker keeps for that
   * version, and asks the worker whether it is serving normally. Null when the manifest cannot be
   * fetched.
   */
  async verify(manifest?: FullManifest): Promise<OfflinePackVerification | null> {
    const target = manifest ?? (await this.fetchFullManifest());
    const storage = this.environment.caches;
    if (!target || !storage) {
      return null;
    }

    const scopePath = await this.scopePath();
    const missing: string[] = [];
    try {
      for (const group of target.groups) {
        const name = `ngsw:${scopePath}:${target.hash}:assets:${group.name}:cache`;
        // `has` first: opening would create an empty cache under Angular's prefix.
        const cached = (await storage.has(name))
          ? new Set((await (await storage.open(name)).keys()).map((request) => new URL(request.url).pathname))
          : new Set<string>();
        for (const url of group.urls) {
          if (!cached.has(new URL(url, this.environment.baseUrl()).pathname)) {
            missing.push(url);
          }
        }
      }
    } catch {
      return { complete: false, missing: target.groups.flatMap((group) => group.urls), manifest: target };
    }

    const degraded = missing.length === 0 && isDegraded(await this.driverState());
    return { complete: missing.length === 0 && !degraded, missing, manifest: target };
  }

  private async awaitFullPack(): Promise<
    { readonly ok: true; readonly manifest: FullManifest } | { readonly ok: false; readonly failure: OfflinePackFailure }
  > {
    const failed = new Set<string>();
    let ready: Extract<VersionEvent, { type: 'VERSION_READY' }> | null = null;
    const subscription: Subscription | undefined = this.swUpdate?.versionUpdates.subscribe((event) => {
      if (event.type === 'VERSION_INSTALLATION_FAILED') {
        failed.add(event.version.hash);
      } else if (event.type === 'VERSION_READY') {
        ready = event;
      }
    });

    try {
      const manifest = await this.fetchFullManifest();
      if (!manifest) {
        return { ok: false, failure: 'UNREACHABLE' };
      }

      await this.ensureControlled();
      let checkSettled = false;
      const check = this.checkForUpdate().then(() => (checkSettled = true));

      const deadline = this.environment.now() + OFFLINE_PACK_READY_TIMEOUT_MS;
      let activated = false;
      for (;;) {
        if (failed.has(manifest.hash)) {
          return { ok: false, failure: 'INSTALL_FAILED' };
        }

        const event = ready as Extract<VersionEvent, { type: 'VERSION_READY' }> | null;
        if (!activated && event && event.latestVersion.hash === manifest.hash) {
          activated = true;
          // Only the pack choice changed: this tab can switch to the full version without a reload.
          // A different build is left to the "update ready" control, which reloads.
          if (isSameBuild(event)) {
            await this.withTimeout(this.swUpdate!.activateUpdate(), OFFLINE_PACK_CHECK_TIMEOUT_MS);
          }
        }

        const verification = await this.verify(manifest);
        if (verification?.complete) {
          // The worker announces a ready build before it completes the check: once the check is
          // done, a VERSION_READY of a different build has reached this tab (see `reloadRequired`).
          await check;
          return { ok: true, manifest };
        }

        // A worker's first install reports no VERSION_INSTALLATION_FAILED: a failed prefetch only
        // leaves it degraded. Once this check has run, a degraded worker will not finish on its own.
        if (checkSettled && isDegraded(await this.driverState())) {
          return { ok: false, failure: 'INSTALL_FAILED' };
        }

        if (this.environment.now() >= deadline) {
          return { ok: false, failure: 'TIMEOUT' };
        }
        await this.environment.delay(OFFLINE_PACK_POLL_MS);
      }
    } finally {
      subscription?.unsubscribe();
    }
  }

  /** The deployed full manifest, fetched past the worker, and the hash Angular's worker files it under. */
  private async fetchFullManifest(): Promise<FullManifest | null> {
    let manifest: ManifestJson;
    try {
      const response = await this.environment.fetch(this.resolve(`${FULL_MANIFEST_PATH}?ngsw-bypass=true`), {
        cache: 'no-store',
        credentials: 'same-origin',
      });
      if (!response.ok) {
        return null;
      }
      manifest = (await response.json()) as ManifestJson;
    } catch {
      return null;
    }

    return toFullManifest(manifest, await angularManifestHash(manifest));
  }

  /**
   * The full version Angular's worker currently serves new tabs from: its latest version, read from
   * the control table it keeps in Cache Storage (`ngsw:<scope>:db:control`, entries `/latest` and
   * `/manifests`). Null when there is none, it cannot be read, or it is the lite manifest.
   */
  private async servedManifest(): Promise<FullManifest | null> {
    const storage = this.environment.caches;
    if (!storage) {
      return null;
    }

    try {
      const name = `ngsw:${await this.scopePath()}:db:control`;
      // `has` first: opening would create an empty cache under Angular's prefix.
      if (!(await storage.has(name))) {
        return null;
      }
      const control = await storage.open(name);
      const origin = new URL(this.environment.baseUrl()).origin;
      const latest = (await (await control.match(`${origin}/latest`))?.json()) as { latest?: unknown } | undefined;
      const manifests = (await (await control.match(`${origin}/manifests`))?.json()) as
        | Record<string, ManifestJson | undefined>
        | undefined;
      const hash = typeof latest?.latest === 'string' ? latest.latest : null;
      const manifest = hash ? manifests?.[hash] : undefined;
      if (!hash || !manifest || (manifest.appData as { offlinePack?: unknown } | undefined)?.offlinePack === 'lite') {
        return null;
      }

      const served = toFullManifest(manifest, hash);
      return served.groups.length > 0 ? served : null;
    } catch {
      return null;
    }
  }

  private recordVerified(manifest: FullManifest, persisted: boolean | null): OfflinePackStatus {
    const previous = this.statusSignal();
    const now = new Date(this.environment.now()).toISOString();
    const changed = !previous || previous.state !== 'ready' || previous.manifestHash !== manifest.hash;
    const status: OfflinePackStatus = {
      state: 'ready',
      buildId: manifest.buildId,
      manifestHash: manifest.hash,
      persisted,
      lastCheckedAt: now,
      lastUpdatedAt: changed ? now : previous.lastUpdatedAt,
    };
    this.setStatus(status);
    return status;
  }

  private setStatus(status: OfflinePackStatus | null): void {
    this.statusStorage.savePackStatus(status);
    this.statusSignal.set(status);
  }

  private async storageIsLow(): Promise<boolean> {
    const storage = this.environment.storage;
    if (!storage || typeof storage.estimate !== 'function') {
      return false;
    }

    try {
      const { quota, usage } = await storage.estimate();
      return typeof quota === 'number' && quota - (usage ?? 0) < OFFLINE_PACK_MIN_FREE_BYTES;
    } catch {
      return false;
    }
  }

  /** Best effort: browsers decide by their own rules, and Safari may still evict an unused site. */
  private async requestPersistence(): Promise<boolean | null> {
    const storage = this.environment.storage;
    if (!storage || typeof storage.persist !== 'function') {
      return null;
    }

    try {
      return (await storage.persisted?.()) || (await storage.persist());
    } catch {
      return null;
    }
  }

  /**
   * The app registers its worker 30 s after it settles; enabling may come sooner. Registering the
   * same script now is what the app would do anyway, and the worker takes control as it activates.
   */
  private async ensureControlled(): Promise<void> {
    const container = this.environment.serviceWorker;
    if (!container || container.controller) {
      return;
    }

    try {
      await container.register(this.resolve(APP_WORKER_SCRIPT));
    } catch {
      return;
    }

    await new Promise<void>((resolve) => {
      const done = (): void => {
        clearTimeout(timer);
        container.removeEventListener('controllerchange', done);
        resolve();
      };
      const timer = setTimeout(done, OFFLINE_PACK_CONTROL_TIMEOUT_MS);
      container.addEventListener('controllerchange', done);
      if (container.controller) {
        done();
      }
    });
  }

  private async scopePath(): Promise<string> {
    try {
      const registration = await this.environment.serviceWorker?.getRegistration();
      if (registration?.scope) {
        return new URL(registration.scope).pathname;
      }
    } catch {
      // Fall back to the app's base, which is the scope Angular registers.
    }
    return new URL(this.environment.baseUrl()).pathname;
  }

  /** Angular's worker reports its state on its debug page; `EXISTING_CLIENTS_ONLY` follows a failed install. */
  private async driverState(): Promise<string | null> {
    if (!this.environment.serviceWorker?.controller) {
      return null;
    }

    try {
      const response = await this.environment.fetch(this.resolve('ngsw/state'), { cache: 'no-store' });
      return /Driver state: (\w+)/.exec(await response.text())?.[1] ?? null;
    } catch {
      return null;
    }
  }

  private resolve(path: string): string {
    return new URL(path, this.environment.baseUrl()).href;
  }

  private withTimeout<T>(promise: Promise<T>, ms: number): Promise<T | null> {
    return new Promise<T | null>((resolve) => {
      const timer = setTimeout(() => resolve(null), ms);
      promise.then(
        (value) => {
          clearTimeout(timer);
          resolve(value);
        },
        () => {
          clearTimeout(timer);
          resolve(null);
        },
      );
    });
  }
}

function toFullManifest(manifest: ManifestJson, hash: string): FullManifest {
  const groups = (manifest.assetGroups ?? [])
    .filter((group) => group.installMode === 'prefetch' && typeof group.name === 'string' && Array.isArray(group.urls))
    .map((group) => ({ name: group.name as string, urls: (group.urls as unknown[]).filter((url): url is string => typeof url === 'string') }));
  const buildId = typeof manifest.appData?.buildId === 'string' ? manifest.appData.buildId : null;
  return { hash, buildId, groups };
}

/** Angular's worker after a failed install: it serves tabs it has not seen from the network. */
function isDegraded(driverState: string | null): boolean {
  return driverState === 'EXISTING_CLIENTS_ONLY' || driverState === 'SAFE_MODE';
}

/** True when a version change only switched between the full and lite manifests of one build. */
export function isSameBuild(event: Extract<VersionEvent, { type: 'VERSION_READY' }>): boolean {
  const current = (event.currentVersion.appData as { buildId?: unknown } | undefined)?.buildId;
  const latest = (event.latestVersion.appData as { buildId?: unknown } | undefined)?.buildId;
  return typeof current === 'string' && current === latest;
}
