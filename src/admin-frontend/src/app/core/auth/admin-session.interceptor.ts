import { HttpErrorResponse, HttpResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, lastValueFrom, switchMap, tap, throwError } from 'rxjs';
import { AdminSessionService } from '../../features/auth/admin-session.service';
import { AdminRefreshCoordinator, explicitSessionRecovery } from './admin-refresh-coordinator';

const sessionScoped = /^\/api\/admin\/(auth\/session|invitations|access)(?=$|[/?])/;

export const adminSessionInterceptor: HttpInterceptorFn = (request, next) => {
  if (!sessionScoped.test(request.url)) return next(request);
  const coordinator = inject(AdminRefreshCoordinator);
  const session = inject(AdminSessionService);
  const generation = coordinator.generation();
  if (generation === null) {
    session.settle('uncertain');
    return throwError(() => uncertain(request.url));
  }

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error)) return throwError(() => error);
      return from(coordinator.renew(generation, request.context.get(explicitSessionRecovery))).pipe(
        switchMap((outcome) => {
          if (outcome === 'renewed') {
            return from(coordinator.retry(generation, () => lastValueFrom(next(request)))).pipe(
              catchError((retryError: unknown) => {
                if (isUnauthorized(retryError)) session.settle('ended');
                if (retryError instanceof HttpErrorResponse) return throwError(() => retryError);
                session.settle('uncertain');
                return throwError(() => uncertain(request.url));
              }),
            );
          }
          session.settle(outcome === 'rejected' ? 'ended' : 'uncertain');
          return throwError(() => outcome === 'rejected' ? error : uncertain(request.url));
        }),
      );
    }),
    tap((event) => {
      if (event instanceof HttpResponse && coordinator.generation() !== generation) {
        session.settle('ended');
        throw new HttpErrorResponse({ status: 401, url: request.url });
      }
    }),
  );
};

function isUnauthorized(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && error.status === 401;
}

function uncertain(url: string) {
  return new HttpErrorResponse({ status: 0, statusText: 'Session renewal outcome unknown', url });
}
