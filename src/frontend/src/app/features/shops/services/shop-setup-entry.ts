import { inject } from '@angular/core';
import { type RedirectFunction, Router } from '@angular/router';
import { catchError, defaultIfEmpty, map, of, timeout } from 'rxjs';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { ShopSetupService } from './shop-setup.service';

export const shopSetupEntry: RedirectFunction = () => {
  const context = inject(ShopContextService);
  const permissions = inject(ShopPermissionsService);
  const router = inject(Router);
  const dashboard = router.parseUrl('/dashboard');
  const scope = context.scope();
  if (!scope?.shopId || !permissions.isOwnerOfActiveShop()) return dashboard;

  const setup = router.parseUrl('/shops/setup');
  return inject(ShopSetupService).getPreferences(scope).pipe(
    timeout(3000),
    map((preferences) => preferences.showOnStartup ? setup : dashboard),
    defaultIfEmpty(dashboard),
    catchError(() => of(dashboard)),
  );
};
