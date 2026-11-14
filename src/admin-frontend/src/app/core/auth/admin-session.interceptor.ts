import {
  HttpErrorResponse,
  HttpResponse,
  type HttpEvent,
  type HttpHandlerFn,
  type HttpInterceptorFn,
  type HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { type Observable, catchError, from, lastValueFrom, switchMap, tap, throwError } from 'rxjs';
import { AdminSessionService } from '../../features/auth/admin-session.service';
import { AdminRefreshCoordinator, explicitSessionRecovery } from './admin-refresh-coordinator';

const sessionScoped = /^\/api\/admin\/(auth\/session|invitations|access)(?=$|[/?])/;

export const adminSessionInterceptor: HttpInterceptorFn = (request, next) => {
  if (!sessionScoped.test(request.url)) return next(request);
  const coordinator = inject(AdminRefreshCoordinator);
  const session = inject(AdminSessionService);
  const explicit = request.context.get(explicitSessionRecovery);
  const generation = coordinator.generation();
  if (generation !== null) return send(request, next, coordinator, session, generation);
  // A sign-in is (or was) replacing the cookies: only explicit recovery waits for it to settle.
  if (!explicit) return unconfirmed(session, request.url);
  return from(coordinator.recoverAbandonedSignIn()).pipe(
    switchMap((recovered) => recovered === undefined
      ? unconfirmed(session, request.url)
      : send(request, next, coordinator, session, recovered)),
  );
};

function send(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
  coordinator: AdminRefreshCoordinator,
  session: AdminSessionService,
  generation: string | undefined,
): Observable<HttpEvent<unknown>> {
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
                return unconfirmed(session, request.url);
              }),
            );
          }
          if (outcome === 'rejected') {
            session.settle('ended');
            return throwError(() => error);
          }
          return unconfirmed(session, request.url);
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
}

function unconfirmed(session: AdminSessionService, url: string) {
  session.settle('uncertain');
  return throwError(() => uncertain(url));
}

function isUnauthorized(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && error.status === 401;
}

function uncertain(url: string) {
  return new HttpErrorResponse({ status: 0, statusText: 'Session renewal outcome unknown', url });
}
