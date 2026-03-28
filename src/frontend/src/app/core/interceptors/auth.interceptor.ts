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

          const retryRequest = request.clone({
            setHeaders: { Authorization: `Bearer ${session.accessToken}` },
            context: request.context.set(REFRESH_ATTEMPTED, true),
          });

          return next(retryRequest);
        }),
        catchError((refreshError) => {
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
    || url.startsWith(AUTH_ENDPOINTS.refreshToken)
    || url.startsWith(AUTH_ENDPOINTS.revokeToken);
}

function isAuthRedirectSuppressedEndpoint(url: string): boolean {
  return url.startsWith(SHOP_ENDPOINTS.create);
}
