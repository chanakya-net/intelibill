import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, filter, fromEvent, interval, merge } from 'rxjs';

import {
  SubscriptionStatusService,
  TradingAuthorityReadError,
} from '../../features/billing/status/subscription-status.service';
import { ShopUpdatesSignalRService } from '../services/shop-updates-signalr.service';
import { ShopContextService } from './shop-context.service';
import { shopRestrictionKey } from './shop-scope';

const REFRESH_INTERVAL_MS = 60_000;

@Injectable({ providedIn: 'root' })
export class ShopDataAccessService {
  private readonly document = inject(DOCUMENT);
  private readonly context = inject(ShopContextService);
  private readonly status = inject(SubscriptionStatusService);
  private readonly updates = inject(ShopUpdatesSignalRService);
  private readonly destroy = inject(DestroyRef);
  private pending?: Subscription;

  constructor() {
    effect(() => {
      this.context.scope();
      this.context.transitioning();
      untracked(() => this.refresh());
    });

    const browser = this.document.defaultView;
    if (!browser) return;
    merge(
      fromEvent(this.document, 'visibilitychange'),
      fromEvent(browser, 'focus'),
      fromEvent(browser, 'online'),
      interval(REFRESH_INTERVAL_MS),
      this.updates.authorityChanged$,
      fromEvent<StorageEvent>(browser, 'storage').pipe(filter((event) => {
        const scope = this.context.scope();
        return !!scope && event.key === shopRestrictionKey(scope);
      })),
    ).pipe(takeUntilDestroyed(this.destroy)).subscribe(() => this.refresh());
    this.destroy.onDestroy(() => this.pending?.unsubscribe());
  }

  private refresh(): void {
    this.pending?.unsubscribe();
    const scope = this.context.scope();
    if (!scope?.shopId || this.context.transitioning() || this.document.visibilityState === 'hidden') return;

    this.pending = this.status.tradingAuthority(scope.shopId).subscribe({
      next: (decision) => {
        if (decision?.schemaVersion === 1 && decision.shopId === scope.shopId &&
            decision.enforced === true &&
            Number.isFinite(Date.parse(decision.authorityVerifiedAt)) &&
            Number.isFinite(Date.parse(decision.validUntil))) {
          this.context.setDataRestricted(scope, false);
        }
      },
      error: (error: unknown) => {
        if (!(error instanceof TradingAuthorityReadError) || error.enforced !== true) return;
        // An evaluated trading refusal still conveys the local security decision. An outage has no enforcement metadata.
        this.context.setDataRestricted(scope, error.failure === 'securitySuspended');
      },
    });
  }
}
