import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { ShopPermissionsService } from '../layout/shop-permissions.service';

export const profitLossGuard: CanActivateFn = () => {
  const permissions = inject(ShopPermissionsService);
  const router = inject(Router);

  if (permissions.isOwnerOrManagerOfActiveShop()) {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};
