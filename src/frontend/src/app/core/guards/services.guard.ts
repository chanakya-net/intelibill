import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { ShopPermissionsService } from '../layout/shop-permissions.service';

export const servicesGuard: CanActivateFn = () => {
  const permissions = inject(ShopPermissionsService);
  const router = inject(Router);

  if (permissions.canManageServices()) {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};
