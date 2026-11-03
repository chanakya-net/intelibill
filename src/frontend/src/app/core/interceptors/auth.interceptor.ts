import {
  HttpContextToken,
  HttpErrorResponse,
  HttpEvent,
  HttpEventType,
  HttpInterceptorFn,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';

import { EMPTY, MonoTypeOperatorFunction, Observable, catchError, mergeMap, of, switchMap, throwError } from 'rxjs';

import { API_BASE_URL, AUTH_ENDPOINTS, SHOP_ENDPOINTS } from '../auth/auth.constants';
import { AuthService } from '../auth/auth.service';
import { REQUEST_SCOPE, ScopeMismatchError, StaleScopeError } from '../context/scoped-request';
import { ShopContextService } from '../context/shop-context.service';

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
  const epochAtRequest = shopContext.epoch();

  const accessToken = authService.getAccessToken();
  // The shop this request is about to be authorized for. If a refresh moves the session to a
  // different shop, replaying the request there would act on the wrong shop's data.
  const shopAtRequest = authService.session()?.activeShopId ?? null;
  // Who the request was made as. A renewal can finish after this tab moved to another account;
  // replaying the request then would act as someone who never made it.
  const userAtRequest = authService.session()?.user?.id ?? null;
  const authorizedRequest = accessToken
    ? request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })
    : request;

  const answer = next(authorizedRequest).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const shouldRedirectOnAuthFailure = !isAuthRedirectSuppressedEndpoint(request.url);

      const shouldTryRefresh = error.status === 401
        && !request.context.get(REFRESH_ATTEMPTED)
        && !isRefreshExcludedEndpointRequest(request.url)
        && authService.hasRefreshToken();

      if (!shouldTryRefresh) {
        return throwError(() => error);
      }

      // Set when the tab is now signed in as someone else: the failure is reported, but the session
      // this tab holds belongs to that other account and is not this request's to clear.
      let tabMovedToAnotherAccount = false;

      return authService.refreshAccessToken().pipe(
        switchMap((session) => {
          if (!session) {
            if (shouldRedirectOnAuthFailure) {
              authService.clearSession();
              return of(router.navigateByUrl('/login')).pipe(
                switchMap(() => throwError(() => error))
              );
            }

            return throwError(() => error);
          }

          if ((session.user?.id ?? null) !== userAtRequest) {
            tabMovedToAnotherAccount = true;
            return throwError(() => error);
          }

          if ((session.activeShopId ?? null) !== shopAtRequest) {
            // The membership this request was authorized under is gone; the renewed session is
            // scoped elsewhere. Surface the failure instead of committing it against another shop.
            return throwError(() => error);
          }

          const retryRequest = request.clone({
            setHeaders: { Authorization: `Bearer ${session.accessToken}` },
            context: request.context.set(REFRESH_ATTEMPTED, true),
          });

          return next(retryRequest);
        }),
        catchError((refreshError) => {
          if (tabMovedToAnotherAccount) {
            return throwError(() => refreshError);
          }

          if (shouldRedirectOnAuthFailure) {
            authService.clearSession();
            return of(router.navigateByUrl('/login')).pipe(
              switchMap(() => throwError(() => refreshError))
            );
          }

          return throwError(() => refreshError);
        })
      );
    })
  );

  return installsScope(request.url)
    ? answer
    : answer.pipe(ignoreAnswersFromLeftScope(() => shopContext.epoch() !== epochAtRequest, owner !== null));
};

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
      mergeMap((event) => (event.type === HttpEventType.Response && scopeMoved() ? leftScope() : of(event))),
    );
}

/** Requests whose answer is itself a session or scope to install, judged where it is installed. */
function installsScope(url: string): boolean {
  return url.startsWith(`${API_BASE_URL}/auth/`)
    || url === SHOP_ENDPOINTS.switch
    || url === SHOP_ENDPOINTS.create;
}

function isRefreshExcludedEndpointRequest(url: string): boolean {
  return url.startsWith(AUTH_ENDPOINTS.loginWithEmail)
    || url.startsWith(AUTH_ENDPOINTS.confirmPasswordReset)
    || url.startsWith(AUTH_ENDPOINTS.refreshToken)
    || url.startsWith(AUTH_ENDPOINTS.revokeToken);
}

function isAuthRedirectSuppressedEndpoint(url: string): boolean {
  return url.startsWith(SHOP_ENDPOINTS.create);
}
