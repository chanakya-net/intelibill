import { inject } from '@angular/core';
import { type CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AdminSessionService } from './admin-session.service';

/** Only a definitive end goes to sign-in; an unconfirmed session stays put and offers a retry. */
export const requireAdminSession: CanActivateFn = () => {
  const session = inject(AdminSessionService);
  const router = inject(Router);
  return session.check().pipe(map((result) => result !== 'ended' || router.createUrlTree(['/auth/sign-in'])));
};
