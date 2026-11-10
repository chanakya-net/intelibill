import { DestroyRef, Injectable, InjectionToken, Injector, inject, signal } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';

import { AuthService } from '../auth/auth.service';
import { ShopContextService } from '../context/shop-context.service';
import { AppReloadService } from '../services/app-reload.service';
import { NetworkStatusService } from '../services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../storage/offline-sales-snapshot-indexeddb.service';
import { OfflinePackService, isSameBuild } from './offline-pack.service';

/** How often a visible tab with the API reachable looks for a new version. */
export const UPDATE_CHECK_INTERVAL_MS = 30 * 60 * 1000;

/**
 * How old the offline data snapshot of the active shop may get on an enabled device before the
 * same schedule refreshes it. Offline billing refuses a snapshot older than 48 hours
 * (`MAX_OFFLINE_AGE_MS` in `offline-sale-state.service.ts`); refreshing after 6 hours keeps a device
 * that was online during the day well inside that, without streaming the catalog every half hour.
 */
export const SNAPSHOT_REFRESH_AGE_MS = 6 * 60 * 60 * 1000;

export interface UpdateScheduleEnvironment {
  readonly window: Pick<Window, 'addEventListener' | 'removeEventListener'> | null;
  readonly document: Pick<Document, 'addEventListener' | 'removeEventListener' | 'visibilityState'> | null;
  readonly setInterval: (callback: () => void, ms: number) => unknown;
  readonly clearInterval: (handle: unknown) => void;
}

export const UPDATE_SCHEDULE_ENVIRONMENT = new InjectionToken<UpdateScheduleEnvironment>('UPDATE_SCHEDULE_ENVIRONMENT', {
  providedIn: 'root',
  factory: () => ({
    window: typeof window === 'undefined' ? null : window,
    document: typeof document === 'undefined' ? null : document,
    setInterval: (callback, ms) => setInterval(callback, ms),
    clearInterval: (handle) => clearInterval(handle as ReturnType<typeof setInterval>),
  }),
});

/**
 * Keeps the app, and on an offline-enabled device its offline pack and data snapshot, current.
 *
 * Checks for a new version at start, every 30 minutes while the tab is visible and the API
 * reachable, when the browser comes back online and when the tab becomes visible; one check at a
 * time. A new build is never applied during use: it is offered as "update ready" and applied through
 * `ShopContextService.prepareToLeave()` (drafts saved under their owner) and a reload, and the next
 * app start picks it up anyway. A version change that only switches between the full and lite
 * manifests of the same build is activated at once (same files, no reload).
 *
 * On a device where some shop has offline billing enabled, the same schedule re-verifies the pack
 * (shown as "offline files incomplete" when it is not whole) and refreshes the active shop's data
 * snapshot once it is older than `SNAPSHOT_REFRESH_AGE_MS`. Invoice leases are left alone.
 * Does nothing without a service worker (development builds).
 * See docs/frontend-architecture.md § Offline Pack.
 */
@Injectable({ providedIn: 'root' })
export class OfflinePackUpdateService {
  private readonly swUpdate = inject(SwUpdate, { optional: true });
  private readonly pack = inject(OfflinePackService);
  private readonly network = inject(NetworkStatusService);
  private readonly shopContext = inject(ShopContextService);
  private readonly appReload = inject(AppReloadService);
  private readonly auth = inject(AuthService);
  private readonly settingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly injector = inject(Injector);
  private readonly snapshotDb = inject(OfflineSalesSnapshotIndexedDbService);
  private readonly schedule = inject(UPDATE_SCHEDULE_ENVIRONMENT);
  private readonly destroyRef = inject(DestroyRef);

  private readonly updateReadySignal = signal(false);
  private readonly unrecoverableSignal = signal(false);
  private readonly applyingSignal = signal(false);
  private readonly applyErrorKeySignal = signal<string | null>(null);

  /** A new build is downloaded and waits for the person to apply it. */
  readonly updateReady = this.updateReadySignal.asReadonly();
  /** The running version can no longer load its files: reload while online. */
  readonly unrecoverable = this.unrecoverableSignal.asReadonly();
  readonly applying = this.applyingSignal.asReadonly();
  /** Why the last apply did not go ahead (translation key), or null. */
  readonly applyErrorKey = this.applyErrorKeySignal.asReadonly();

  private started = false;
  private running: Promise<void> | null = null;

  /** Starts in the background; returns at once so app start never waits for it. */
  start(): void {
    if (this.started || !this.swUpdate?.isEnabled) {
      return;
    }
    this.started = true;
    const swUpdate = this.swUpdate;

    const versions = swUpdate.versionUpdates.subscribe((event) => {
      if (event.type === 'VERSION_READY') {
        if (isSameBuild(event)) {
          // Only the pack choice changed (another tab enabled or disabled offline): same files.
          void swUpdate.activateUpdate().catch(() => false);
        } else {
          this.updateReadySignal.set(true);
        }
      } else if (event.type === 'VERSION_INSTALLATION_FAILED' && this.pack.deviceNeedsPack()) {
        // The worker may serve new tabs from the network until it restarts: not a working pack.
        this.pack.markIncomplete();
      }
    });
    const unrecoverable = swUpdate.unrecoverable.subscribe(() => this.unrecoverableSignal.set(true));

    const onOnline = (): void => void this.runCycle();
    const onVisibility = (): void => {
      if (this.schedule.document?.visibilityState === 'visible') {
        void this.runCycle();
      }
    };
    this.schedule.window?.addEventListener('online', onOnline);
    this.schedule.document?.addEventListener('visibilitychange', onVisibility);
    const timer = this.schedule.setInterval(() => {
      if (this.schedule.document?.visibilityState === 'visible' && this.network.canReachApi()) {
        void this.runCycle();
      }
    }, UPDATE_CHECK_INTERVAL_MS);

    this.destroyRef.onDestroy(() => {
      versions.unsubscribe();
      unrecoverable.unsubscribe();
      this.schedule.window?.removeEventListener('online', onOnline);
      this.schedule.document?.removeEventListener('visibilitychange', onVisibility);
      this.schedule.clearInterval(timer);
    });

    // The flag first, so the worker's first manifest read already sees this device's choice.
    void this.pack.reconcile().then(() => this.runCycle());
  }

  /** One check at a time; a trigger that arrives during a check shares it. */
  runCycle(): Promise<void> {
    this.running ??= this.cycle().finally(() => {
      this.running = null;
    });
    return this.running;
  }

  /**
   * Applies a downloaded build: saves every draft under its owner first, then activates and reloads.
   * Refused (with a message) while a write waits for its answer or a draft could not be saved.
   */
  async applyUpdate(): Promise<void> {
    if (this.applyingSignal() || !this.swUpdate?.isEnabled) {
      return;
    }

    this.applyingSignal.set(true);
    this.applyErrorKeySignal.set(null);
    try {
      const leave = await this.shopContext.prepareToLeave();
      if (!leave.ok) {
        this.applyErrorKeySignal.set('shell.appUpdate.busy');
        return;
      }

      await this.swUpdate.activateUpdate().catch(() => false);
      this.appReload.reloadPage();
    } finally {
      this.applyingSignal.set(false);
    }
  }

  reload(): void {
    this.appReload.reloadPage();
  }

  private async cycle(): Promise<void> {
    try {
      await this.pack.checkForUpdate();
      if (!this.pack.deviceNeedsPack()) {
        return;
      }

      await this.pack.refreshStatus();
      await this.refreshSnapshotIfStale();
    } catch {
      // A failed cycle changes nothing; the next trigger tries again.
    }
  }

  private async refreshSnapshotIfStale(): Promise<void> {
    const shopId = this.auth.session()?.activeShopId;
    if (!shopId || !this.network.canReachApi()) {
      return;
    }

    const settings = this.settingsStorage.loadSettings(shopId);
    if (!settings?.enabled) {
      return;
    }

    const completedAt = Date.parse(settings.lastCompleteSnapshotAt ?? '');
    if (Number.isFinite(completedAt) && Date.now() - completedAt < SNAPSHOT_REFRESH_AGE_MS) {
      return;
    }

    // Loaded on first use: only an enabled device with an old snapshot needs the stream reader, and
    // this service starts with the app.
    const { OfflineSalesSnapshotSyncService } = await import('../../features/sales/services/offline-sales-snapshot-sync.service');
    await this.injector.get(OfflineSalesSnapshotSyncService).syncForShop(shopId);
    const usable = await this.snapshotDb.getUsableSnapshotInfo(shopId);
    if (usable?.completedAt && usable.completedAt !== settings.lastCompleteSnapshotAt) {
      this.settingsStorage.updateSettings(shopId, (current) => ({ ...current, lastCompleteSnapshotAt: usable.completedAt }));
    }
  }
}
