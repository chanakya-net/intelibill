import { HttpErrorResponse, HttpEvent, HttpRequest, HttpResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AUTH_ENDPOINTS } from '../auth/auth.constants';
import { AuthService } from '../auth/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  const sessionSignal = signal<{ activeShopId: string | null; user?: { id: string } } | null>(null);
  const mockAuthService = {
    getAccessToken: vi.fn<() => string | null>(),
    hasRefreshToken: vi.fn<() => boolean>(),
    refreshAccessToken: vi.fn(),
    clearSession: vi.fn(),
    applyAuthResult: vi.fn(),
    session: sessionSignal,
  };

  const mockRouter = { navigateByUrl: vi.fn().mockResolvedValue(true) };

  beforeEach(() => {
    mockAuthService.getAccessToken.mockReset();
    mockAuthService.hasRefreshToken.mockReset();
    mockAuthService.refreshAccessToken.mockReset();
    mockAuthService.clearSession.mockReset();
    mockRouter.navigateByUrl.mockReset();
    sessionSignal.set(null);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: mockAuthService },
        { provide: Router, useValue: mockRouter },
      ],
    });
  });

  function runInterceptor(req: HttpRequest<unknown>, nextFn: (r: HttpRequest<unknown>) => Observable<HttpEvent<unknown>>) {
    return TestBed.runInInjectionContext(() => authInterceptor(req, nextFn as Parameters<typeof authInterceptor>[1]));
  }

  it('adds Authorization header when access token is present', () => {
    mockAuthService.getAccessToken.mockReturnValue('test-token');

    const req = new HttpRequest('GET', '/api/data');
    let capturedReq: HttpRequest<unknown> | undefined;

    const next = (r: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => {
      capturedReq = r;
      return of(new HttpResponse({ status: 200 }));
    };

    runInterceptor(req, next).subscribe();

    expect(capturedReq?.headers.get('Authorization')).toBe('Bearer test-token');
  });

  it('does not add Authorization header when no access token', () => {
    mockAuthService.getAccessToken.mockReturnValue(null);

    const req = new HttpRequest('GET', '/api/data');
    let capturedReq: HttpRequest<unknown> | undefined;

    const next = (r: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => {
      capturedReq = r;
      return of(new HttpResponse({ status: 200 }));
    };

    runInterceptor(req, next).subscribe();

    expect(capturedReq?.headers.has('Authorization')).toBe(false);
  });

  it('does not trigger refresh or redirect for reset-password confirmation 401 responses', () => {
    mockAuthService.getAccessToken.mockReturnValue('test-token');
    mockAuthService.hasRefreshToken.mockReturnValue(true);

    const req = new HttpRequest('POST', AUTH_ENDPOINTS.confirmPasswordReset, {
      email: 'user@example.com',
      token: 'token-123',
      newPassword: 'Password123!',
    });

    let capturedError: HttpErrorResponse | undefined;
    const next = () =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 401,
            url: AUTH_ENDPOINTS.confirmPasswordReset,
          }),
      );

    runInterceptor(req, next).subscribe({
      error: (error) => {
        capturedError = error;
      },
    });

    expect(capturedError?.status).toBe(401);
    expect(mockAuthService.refreshAccessToken).not.toHaveBeenCalled();
    expect(mockAuthService.clearSession).not.toHaveBeenCalled();
    expect(mockRouter.navigateByUrl).not.toHaveBeenCalled();
  });

  it('replays a request after refresh when the session stays in the same shop', () => {
    mockAuthService.getAccessToken.mockReturnValue('expired-token');
    mockAuthService.hasRefreshToken.mockReturnValue(true);
    sessionSignal.set({ activeShopId: 'shop-a' });
    mockAuthService.refreshAccessToken.mockImplementation(() => {
      sessionSignal.set({ activeShopId: 'shop-a' });
      return of({ accessToken: 'renewed-token', activeShopId: 'shop-a' });
    });

    const attempts: (string | null)[] = [];
    const next = (request: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => {
      attempts.push(request.headers.get('Authorization'));
      return attempts.length === 1
        ? throwError(() => new HttpErrorResponse({ status: 401 }))
        : of(new HttpResponse({ status: 200 }));
    };

    let status: number | undefined;
    runInterceptor(new HttpRequest('POST', '/api/sales', {}), next).subscribe({
      next: (event) => {
        if (event instanceof HttpResponse) {
          status = event.status;
        }
      },
    });

    expect(status).toBe(200);
    expect(attempts).toEqual(['Bearer expired-token', 'Bearer renewed-token']);
  });

  it('does not replay a request when refresh moved the session to another shop', () => {
    mockAuthService.getAccessToken.mockReturnValue('suspended-shop-token');
    mockAuthService.hasRefreshToken.mockReturnValue(true);
    sessionSignal.set({ activeShopId: 'shop-a' });
    // Shop A suspended this membership mid-request, so renewal comes back scoped to shop B.
    mockAuthService.refreshAccessToken.mockImplementation(() => {
      sessionSignal.set({ activeShopId: 'shop-b' });
      return of({ accessToken: 'shop-b-token', activeShopId: 'shop-b' });
    });

    let attempts = 0;
    const next = (): Observable<HttpEvent<unknown>> => {
      attempts++;
      return throwError(() => new HttpErrorResponse({ status: 401 }));
    };

    let failed: HttpErrorResponse | undefined;
    runInterceptor(new HttpRequest('POST', '/api/sales', { total: 500 }), next).subscribe({
      error: (error: HttpErrorResponse) => (failed = error),
    });

    // One attempt only: the sale must not be committed against shop B.
    expect(attempts).toBe(1);
    expect(failed?.status).toBe(401);
  });

  it('does not replay a request as another account when the tab switched user during the renewal', () => {
    mockAuthService.getAccessToken.mockReturnValue('expired-token');
    mockAuthService.hasRefreshToken.mockReturnValue(true);
    sessionSignal.set({ activeShopId: 'shop-a', user: { id: 'user-1' } });
    // While the renewal waited, this browser signed user-1 out and user-2 in, in the same shop.
    mockAuthService.refreshAccessToken.mockImplementation(() => {
      sessionSignal.set({ activeShopId: 'shop-a', user: { id: 'user-2' } });
      return of({ accessToken: 'user-2-token', activeShopId: 'shop-a', user: { id: 'user-2' } });
    });

    let attempts = 0;
    const next = (): Observable<HttpEvent<unknown>> => {
      attempts++;
      return throwError(() => new HttpErrorResponse({ status: 401 }));
    };

    let failed: HttpErrorResponse | undefined;
    runInterceptor(new HttpRequest('POST', '/api/sales', { total: 500 }), next).subscribe({
      error: (error: HttpErrorResponse) => (failed = error),
    });

    expect(attempts).toBe(1);
    expect(failed?.status).toBe(401);
    expect(mockAuthService.clearSession).not.toHaveBeenCalled();
  });
});
