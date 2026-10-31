import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';

import { catchError, of, switchMap, throwError } from 'rxjs';

import { AUTH_ENDPOINTS, SHOP_ENDPOINTS } from '../auth/auth.constants';
import { AuthService } from '../auth/auth.service';

const REFRESH_ATTEMPTED = new HttpContextToken<boolean>(() => false);

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

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

  return next(authorizedRequest).pipe(
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
};

function isRefreshExcludedEndpointRequest(url: string): boolean {
  return url.startsWith(AUTH_ENDPOINTS.loginWithEmail)
    || url.startsWith(AUTH_ENDPOINTS.confirmPasswordReset)
    || url.startsWith(AUTH_ENDPOINTS.refreshToken)
    || url.startsWith(AUTH_ENDPOINTS.revokeToken);
}

function isAuthRedirectSuppressedEndpoint(url: string): boolean {
  return url.startsWith(SHOP_ENDPOINTS.create);
}
