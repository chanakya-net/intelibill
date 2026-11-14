import { inject } from '@angular/core';
import { type CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AdminSessionService } from './admin-session.service';

export const requireAdminSession: CanActivateFn = () => {
  const session = inject(AdminSessionService);
  const router = inject(Router);
  return session.check().pipe(map((active) => active || router.createUrlTree(['/auth/sign-in'])));
};
