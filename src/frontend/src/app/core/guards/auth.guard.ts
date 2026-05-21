import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { from, of, switchMap } from 'rxjs';

import { AuthService } from '../auth/auth.service';

export const authGuard: CanActivateFn = (route) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  return authService.bootstrapSessionWithStatus().pipe(
    switchMap((status) => {
      if (status === 'READY') {
        return of(true);
      }

      if (status === 'API_UNREACHABLE' && route.data?.['allowOfflineSalesGrace'] === true) {
        return from(authService.canUseOfflineSalesAuthGrace()).pipe(
          switchMap((canUseGrace) => of(canUseGrace ? true : router.createUrlTree(['/login'])))
        );
      }

      return of(router.createUrlTree(['/login']));
    })
  );
};
