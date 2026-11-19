import { DestroyRef, Injectable, inject } from '@angular/core';
import { Subject, filter } from 'rxjs';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { sameOwner, type ScopeOwner } from '../../../core/context/shop-scope';

/** A shell refusal can also affect a sample open in a separate print tab. */
@Injectable({ providedIn: 'root' })
export class ShopSetupSecurityEvents {
  private readonly context = inject(ShopContextService);
  private readonly refused = new Subject<ScopeOwner>();
  private readonly channel = typeof BroadcastChannel === 'undefined' ? null :
    new BroadcastChannel('intelibill.shop-setup.security-refused');

  constructor() {
    if (this.channel) {
      this.channel.onmessage = ({ data }) => {
        if (typeof data?.userId === 'string' && typeof data.shopId === 'string') this.refused.next(data);
      };
    }
    inject(DestroyRef).onDestroy(() => {
      this.channel?.close();
      this.refused.complete();
    });
  }

  refuse(shopId: string): void {
    const scope = this.context.scope();
    if (!scope) return;
    const target = { userId: scope.userId, shopId };
    this.refused.next(target);
    this.channel?.postMessage(target);
  }

  forMountedShop() {
    const mounted = this.context.scope();
    return this.refused.pipe(filter(target => !!mounted && sameOwner(target, mounted) &&
      this.context.isCurrent(mounted) && this.context.epoch() === mounted.epoch));
  }
}
