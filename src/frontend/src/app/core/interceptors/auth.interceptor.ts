import {
  HttpContextToken,
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpRequest,
  HttpInterceptorFn,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';

import { problemCode } from '@intelibill/api';
import { EMPTY, MonoTypeOperatorFunction, Observable, catchError, from, mergeMap, of, switchMap, throwError } from 'rxjs';

import { API_BASE_URL, AUTH_ENDPOINTS, SHOP_ENDPOINTS } from '../auth/auth.constants';
import { AuthService } from '../auth/auth.service';
import { REQUEST_SCOPE, ScopeMismatchError, StaleScopeError } from '../context/scoped-request';
import { ShopContextService } from '../context/shop-context.service';
import { SERVICE_WORKER_BYPASS_HEADER, classifyApiRequest } from '../service-worker/service-worker-bypass';

const REFRESH_ATTEMPTED = new HttpContextToken<boolean>(() => false);

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const shopContext = inject(ShopContextService);

  // A write frozen for one user and shop is never sent with another scope's token.
  const owner = request.context.get(REQUEST_SCOPE);
  if (owner && !shopContext.isCurrent(owner)) {
    return throwError(() => new ScopeMismatchError());
  }
  // Credentials and the worker bypass are for this application's API only. Anything else (static
  // assets, a third-party URL) goes out exactly as asked: no token to leak, no refresh to trigger.
  const routing = classifyApiRequest(request.url, API_BASE_URL, document.baseURI);
  if (!routing.isApiRequest) {
    return next(request);
  }

  const epochAtRequest = shopContext.epoch();
  const invocation = shopContext.scope();
  const path = canonicalPath(request.url);
  const scoped = !isSessionInstalling(request.method, path);
  const scopeMoved = () => shopContext.epoch() !== epochAtRequest;

  const workerBypass: Record<string, string> = routing.bypassServiceWorker
    ? { [SERVICE_WORKER_BYPASS_HEADER]: 'true' }
    : {};
  const answer = sendWithRefresh(request, next, authService, router, workerBypass, path, scopeMoved);

  if (!scoped) return answer;
  return answer.pipe(
    catchError((error: unknown) => {
      if (!invocation || scopeMoved() || !authService.getAccessToken() || !canReportRefusal(path, invocation.shopId) ||
          !(error instanceof HttpErrorResponse) || error.status !== 403) {
        return throwError(() => error);
      }

      return observeSecurityRefusal(error, () => {
        if (!scopeMoved()) shopContext.setDataRestricted(invocation, true);
      });
    }),
    ignoreAnswersFromLeftScope(scopeMoved, owner !== null),
  );
};

function sendWithRefresh(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
  auth: AuthService,
  router: Router,
  workerBypass: Record<string, string>,
  path: string,
  scopeMoved: () => boolean,
): Observable<HttpEvent<unknown>> {
  const sessionAtRequest = auth.session();
  const accessToken = auth.getAccessToken();
  const authorized = request.clone({
    setHeaders: accessToken ? { ...workerBypass, Authorization: `Bearer ${accessToken}` } : workerBypass,
  });

  return next(authorized).pipe(catchError((error: unknown) => {
    if (!(error instanceof HttpErrorResponse) || error.status !== 401 || scopeMoved() ||
        request.context.get(REFRESH_ATTEMPTED) || isRefreshExcludedEndpointRequest(path) || !auth.hasRefreshToken()) {
      return throwError(() => error);
    }

    let canClearSession = !isAuthRedirectSuppressedEndpoint(path);
    return auth.refreshAccessToken().pipe(
      switchMap((session) => {
        if (scopeMoved() || !session) return throwError(() => error);
        if ((session.user?.id ?? null) !== (sessionAtRequest?.user?.id ?? null) ||
            (session.activeShopId ?? null) !== (sessionAtRequest?.activeShopId ?? null)) {
          canClearSession = false;
          return throwError(() => error);
        }

        const retry = request.clone({
          setHeaders: { ...workerBypass, Authorization: `Bearer ${session.accessToken}` },
          context: request.context.set(REFRESH_ATTEMPTED, true),
        });
        return next(retry).pipe(catchError((replayError: unknown) => {
          canClearSession &&= replayError instanceof HttpErrorResponse && replayError.status === 401;
          return throwError(() => replayError);
        }));
      }),
      catchError((failure: unknown) => {
        if (!canClearSession || scopeMoved()) return throwError(() => failure);
        auth.clearSession();
        return of(router.navigateByUrl('/login')).pipe(switchMap(() => throwError(() => failure)));
      }),
    );
  }));
}

function observeSecurityRefusal(error: HttpErrorResponse, observed: () => void): Observable<never> {
  const finish = (problem: unknown): Observable<never> => {
    if (problemCode(problem) === 'Licence.SecuritySuspended') observed();
    return throwError(() => error);
  };
  if (!(error.error instanceof Blob)) return finish(error.error);

  const decoded = (error.error as Blob).text().then((body) => JSON.parse(body) as unknown).catch(() => null);
  return from(decoded).pipe(switchMap(finish));
}

/**
 * An answer that arrives after the tab moved to another user or shop belongs to the scope that was
 * left: the server may have acted on it there, but nothing about it may reach the new scope's views.
 * Owner-bound writes get `StaleScopeError` so they can keep their outcome under their owner; every
 * other request completes without a value.
 */
function ignoreAnswersFromLeftScope(
  scopeMoved: () => boolean,
  ownerBound: boolean,
): MonoTypeOperatorFunction<HttpEvent<unknown>> {
  const leftScope = (): Observable<never> => (ownerBound ? throwError(() => new StaleScopeError()) : EMPTY);

  return (source) =>
    source.pipe(
      catchError((error: unknown) => (scopeMoved() ? leftScope() : throwError(() => error))),
      mergeMap((event) => (scopeMoved() ? leftScope() : of(event))),
    );
}

/** Answers that are themselves a session to install (or end); judged where they are installed. */
const SESSION_INSTALLING_ENDPOINTS: readonly string[] = [
  AUTH_ENDPOINTS.login,
  AUTH_ENDPOINTS.confirmEmailEnrollment,
  AUTH_ENDPOINTS.confirmEmailClaim,
  AUTH_ENDPOINTS.confirmPhoneEnrollment,
  AUTH_ENDPOINTS.confirmPhoneLogin,
  AUTH_ENDPOINTS.refreshToken,
  AUTH_ENDPOINTS.revokeToken,
  SHOP_ENDPOINTS.switch,
  SHOP_ENDPOINTS.create,
];

function canonicalPath(url: string): string {
  return new URL(url, document.baseURI).pathname.replace(/\/+$/, '').toLowerCase();
}

function isSessionInstalling(method: string, path: string): boolean {
  return method === 'POST' && SESSION_INSTALLING_ENDPOINTS.some((endpoint) => canonicalPath(endpoint) === path);
}

function canReportRefusal(path: string, activeShopId: string): boolean {
  const base = canonicalPath(API_BASE_URL);
  const route = path.slice(base.length + 1);
  const [resource, target] = route.split('/');
  if (resource === 'licensing' || resource === 'billing' || resource === 'auth') return false;
  if (resource !== 'shops' || !target || target === 'me' || target === 'default') return true;
  return target === activeShopId.toLowerCase();
}

function isRefreshExcludedEndpointRequest(path: string): boolean {
  return [
    AUTH_ENDPOINTS.loginWithEmail, AUTH_ENDPOINTS.confirmPasswordReset,
    AUTH_ENDPOINTS.refreshToken, AUTH_ENDPOINTS.revokeToken,
  ].some((endpoint) => path.startsWith(canonicalPath(endpoint)));
}

function isAuthRedirectSuppressedEndpoint(path: string): boolean {
  return path.startsWith(canonicalPath(SHOP_ENDPOINTS.create));
}
