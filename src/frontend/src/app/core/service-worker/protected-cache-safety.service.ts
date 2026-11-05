import { Injectable, inject, signal } from '@angular/core';

import { ShopContextService } from '../context/shop-context.service';
import { AppReloadService } from '../services/app-reload.service';
import { CacheSafetyState, ProtectedCacheMigrationService } from './protected-cache-migration.service';

/**
 * What the application knows about cached protected answers in this browser, for the shell to show.
 *
 * Kept apart from the session: requests are not held back on it, because every API request from
 * this build bypasses the service worker whatever this says. It decides only whether the shell
 * offers a reload to finish the cleanup.
 */
@Injectable({ providedIn: 'root' })
export class ProtectedCacheSafetyService {
  private readonly migration = inject(ProtectedCacheMigrationService);
  private readonly shopContext = inject(ShopContextService);
  private readonly appReload = inject(AppReloadService);

  private readonly stateSignal = signal<CacheSafetyState | 'checking'>('checking');
  private started = false;
  private checking = false;
  private checkAgain = false;

  readonly state = this.stateSignal.asReadonly();

  /**
   * Checks in the background now and again whenever another worker takes control of this page (a
   * deployment mid-rollout can briefly serve an earlier one). Each change of user or shop also
   * removes answers stored by an earlier worker since: that keeps them off disk, though only
   * replacing the worker stops one that is still running from replaying what it holds.
   */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    this.shopContext.onDetach(() => this.purgeOnScopeChange());
    this.migration.watchWorkerChanges(() => this.check());
    this.check();
  }

  reload(): void {
    this.appReload.reloadPage();
  }

  /** One check at a time; a change seen during a check runs one more after it. */
  private check(): void {
    if (this.checking) {
      this.checkAgain = true;
      return;
    }

    this.checking = true;
    this.migration
      .ensureSafeVersion()
      .then(
        (state) => this.stateSignal.set(state),
        () => this.stateSignal.set('blocked'),
      )
      .finally(() => {
        this.checking = false;
        if (this.checkAgain) {
          this.checkAgain = false;
          this.check();
        }
      });
  }

  private purgeOnScopeChange(): Promise<boolean> {
    return this.migration.purgeLegacyCaches().then((purged) => {
      const state = this.stateSignal();
      if (!purged && state !== 'reloadRequired') {
        this.stateSignal.set('blocked');
      } else if (purged && state === 'blocked') {
        this.stateSignal.set('safe');
      }
      return purged;
    });
  }
}
