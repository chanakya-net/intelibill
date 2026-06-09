import { isPlatformBrowser } from '@angular/common';
import { inject, PLATFORM_ID } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { ShopPermissionsService } from '../layout/shop-permissions.service';

export const dashboardGuard: CanActivateFn = () => {
  if (!isPlatformBrowser(inject(PLATFORM_ID))) {
    return true;
  }

  const permissions = inject(ShopPermissionsService);
  const router = inject(Router);

  if (permissions.isOwnerOrManagerOfActiveShop()) {
    return true;
  }

  return router.createUrlTree(['/sales']);
};
