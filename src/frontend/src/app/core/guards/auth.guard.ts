import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { map } from 'rxjs';

import { AuthService } from '../auth/auth.service';

export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  return authService.bootstrapSession().pipe(
    map((isReady) => {
      if (isReady) {
        return true;
      }

      return router.createUrlTree(['/login']);
    })
  );
};
