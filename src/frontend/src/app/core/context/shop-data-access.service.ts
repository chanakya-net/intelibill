import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, computed, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, fromEvent, interval, merge } from 'rxjs';

import { SubscriptionStatusService } from '../../features/billing/status/subscription-status.service';
import { ShopUpdatesSignalRService } from '../services/shop-updates-signalr.service';
import { ShopContextService } from './shop-context.service';
import { shopRestrictionConfirmationKey, shopRestrictionKey } from './shop-scope';

const REFRESH_INTERVAL_MS = 60_000;

@Injectable({ providedIn: 'root' })
export class ShopDataAccessService {
  private readonly document = inject(DOCUMENT);
  private readonly context = inject(ShopContextService);
  private readonly status = inject(SubscriptionStatusService);
  private readonly updates = inject(ShopUpdatesSignalRService);
  private readonly destroy = inject(DestroyRef);
  private pending?: Subscription;
  /** Track only owner identity and transition state; epoch bumps from data-access changes must not trigger a re-read. */
  private readonly activeOwnerKey = computed(() => {
    const scope = this.context.scope();
    return scope?.shopId && !this.context.transitioning() ? `${scope.userId}:${scope.shopId}` : null;
  });

  constructor() {
    effect(() => {
      if (this.activeOwnerKey()) {
        untracked(() => this.refresh());
      }
    });

    const browser = this.document.defaultView;
    if (!browser) return;
    merge(
      fromEvent(this.document, 'visibilitychange'),
      fromEvent(browser, 'focus'),
      fromEvent(browser, 'online'),
      interval(REFRESH_INTERVAL_MS),
      this.updates.authorityChanged$,
    ).pipe(takeUntilDestroyed(this.destroy)).subscribe(() => this.refresh());

    fromEvent<StorageEvent>(browser, 'storage').pipe(takeUntilDestroyed(this.destroy)).subscribe((event) => {
      const scope = this.context.scope();
      if (!scope?.shopId || event.storageArea !== browser.localStorage) return;
      if (event.key === shopRestrictionKey(scope) && event.newValue !== null) this.context.applyRestrictionNotice(scope);
      else if (event.key === shopRestrictionKey(scope) || event.key === shopRestrictionConfirmationKey(scope)) this.refresh();
    });
    this.destroy.onDestroy(() => this.pending?.unsubscribe());
  }

  private refresh(): void {
    const scope = this.context.scope();
    if (!scope?.shopId || this.context.transitioning() || this.document.visibilityState === 'hidden') return;

    this.pending?.unsubscribe();
    this.pending = this.status.tradingAuthority(scope.shopId).subscribe({ error: () => undefined });
  }
}
