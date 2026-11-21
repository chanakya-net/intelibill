import { DestroyRef, inject } from '@angular/core';
import { ShopContextService } from './shop-context.service';

/** A queued print callback can outlive its component or the authority that prepared its document. */
export function injectScopedPrint(): () => void {
  const context = inject(ShopContextService);
  const destroy = inject(DestroyRef);
  const epoch = context.epoch();
  return () => {
    if (!destroy.destroyed && context.epoch() === epoch && !context.dataRestricted()) window.print();
  };
}
