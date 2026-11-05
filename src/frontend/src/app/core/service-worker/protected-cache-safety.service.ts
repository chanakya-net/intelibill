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

  readonly state = this.stateSignal.asReadonly();

  /**
   * Runs the migration once, in the background, and purges again on every change of user or shop:
   * an older build still open in another tab may have stored an answer since.
   */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    this.shopContext.onDetach(() => this.purgeOnScopeChange());
    this.migration.ensureSafeVersion().then(
      (state) => this.stateSignal.set(state),
      () => this.stateSignal.set('blocked'),
    );
  }

  reload(): void {
    this.appReload.reloadPage();
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
