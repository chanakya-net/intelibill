import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { from, map, of, switchMap } from 'rxjs';

import { OfflineInvoiceAccessService } from '../../features/sales/services/offline-invoice-access.service';
import { AuthService } from '../auth/auth.service';
import { authGuard } from './auth.guard';

/**
 * Guards the invoice print route. A server sale prints only for a signed-in session (the ordinary auth guard). A
 * queued offline invoice (`offline=1`) may also print while the API cannot be reached, under the device's offline
 * grace, and only when the signed-in user may read that very invoice on this shop and device. A server that answered
 * and refused the session is a sign-out, never lost connectivity.
 */
export const offlineInvoicePrintGuard: CanActivateFn = (route, state) => {
  if (route.queryParamMap.get('offline') !== '1') {
    return authGuard(route, state);
  }

  const authService = inject(AuthService);
  const router = inject(Router);
  const access = inject(OfflineInvoiceAccessService);

  if (authService.isAuthenticated()) {
    return true;
  }

  return authService.bootstrapSessionWithStatus().pipe(
    switchMap((status) => {
      if (status === 'READY') {
        return of(true);
      }

      if (status === 'API_UNREACHABLE') {
        return from(access.canRead(route.paramMap.get('saleId') ?? '')).pipe(
          map((canRead) => (canRead ? true : router.createUrlTree(['/login']))),
        );
      }

      return of(router.createUrlTree(['/login']));
    }),
  );
};
