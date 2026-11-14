import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AdminSessionService } from '../../features/auth/admin-session.service';
import { AdminRefreshCoordinator } from './admin-refresh-coordinator';

// Requests authorized by the provider access cookie; bootstrap, invitation acceptance and sign-in are not.
const sessionScoped = /^\/api\/admin\/(auth\/session|invitations|access)(?=$|[/?])/;

/**
 * A provider request refused for an expired or missing access token renews the session once and is
 * sent again. A refused renewal ends the session in this tab; an unknown one is reported as a
 * network failure (status 0) so callers keep it distinct from a sign-out.
 */
export const adminSessionInterceptor: HttpInterceptorFn = (request, next) => {
  if (!sessionScoped.test(request.url)) return next(request);
  const coordinator = inject(AdminRefreshCoordinator);
  const session = inject(AdminSessionService);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) return throwError(() => error);
      return from(coordinator.renew()).pipe(
        switchMap((outcome) => {
          if (outcome === 'renewed') return next(request);
          session.settle(outcome === 'rejected' ? 'ended' : 'uncertain');
          return throwError(() => outcome === 'rejected' ? error : uncertain(request.url));
        }),
      );
    }),
  );
};

function uncertain(url: string) {
  return new HttpErrorResponse({ status: 0, statusText: 'Session renewal outcome unknown', url });
}
