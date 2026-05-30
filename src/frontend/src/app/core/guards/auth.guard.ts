import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { from, of, switchMap } from 'rxjs';

import { AuthService } from '../auth/auth.service';

export const authGuard: CanActivateFn = (route, state) => {
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

      const allowOfflineSalesGrace = route.data?.['allowOfflineSalesGrace'] === true;
      const allowOfflineSalesGracePaths = route.data?.['allowOfflineSalesGracePaths'];
      const isOfflineSalesGracePath =
        Array.isArray(allowOfflineSalesGracePaths) &&
        allowOfflineSalesGracePaths.some(
          (path) =>
            typeof path === 'string' &&
            router.parseUrl(path).toString() === router.parseUrl(state.url).toString()
        );

      if (status === 'API_UNREACHABLE' && (allowOfflineSalesGrace || isOfflineSalesGracePath)) {
        return from(authService.canUseOfflineSalesAuthGrace()).pipe(
          switchMap((canUseGrace) => of(canUseGrace ? true : router.createUrlTree(['/login'])))
        );
      }

      return of(router.createUrlTree(['/login']));
    })
  );
};
