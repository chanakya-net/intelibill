import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../auth/auth.service';

export const discountsGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const session = authService.session();
  const activeShop = session?.shops.find((s) => s.isDefault) ?? null;

  if (!activeShop) {
    return router.createUrlTree(['/dashboard']);
  }

  const role = activeShop.role.toLowerCase();
  if (role === 'owner' || role === 'manager') {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};
