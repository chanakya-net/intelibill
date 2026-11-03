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

      // A 401 for a token this tab has already replaced (it left that scope while the request was
      // out) says nothing about the session it holds now: neither renew nor clear it.
      const shouldTryRefresh = error.status === 401
        && shopContext.epoch() === epochAtRequest
        && !request.context.get(REFRESH_ATTEMPTED)
        && !isRefreshExcludedEndpointRequest(request.url)
        && authService.hasRefreshToken();

      if (!shouldTryRefresh) {
        return throwError(() => error);
      }

      // Set when the renewed session is no longer the one this request was made in: another account,
      // or the same account in another shop. The failure is reported, but that session is valid and
      // is not this request's to clear — clearing it would sign every tab out.
      let tabMovedToAnotherAccount = false;
      let requestScopeLeft = false;

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
            // The renewed session is scoped elsewhere (the membership is gone, or another tab moved
            // the device). Surface the failure instead of committing it against another shop.
            requestScopeLeft = true;
            return throwError(() => error);
          }

          const retryRequest = request.clone({
            setHeaders: { Authorization: `Bearer ${session.accessToken}` },
            context: request.context.set(REFRESH_ATTEMPTED, true),
          });

          return next(retryRequest);
        }),
        catchError((refreshError) => {
          if (tabMovedToAnotherAccount || requestScopeLeft) {
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

  return isScopedApiRequest(request.url)
    ? answer.pipe(ignoreAnswersFromLeftScope(() => shopContext.epoch() !== epochAtRequest, owner !== null))
    : answer;
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

/** API data belongs to the scope that asked for it; static assets and session answers do not. */
function isScopedApiRequest(url: string): boolean {
  return url.startsWith(`${API_BASE_URL}/`) && !SESSION_INSTALLING_ENDPOINTS.includes(url);
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
