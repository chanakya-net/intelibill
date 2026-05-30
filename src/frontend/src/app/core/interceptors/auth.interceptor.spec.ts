import { HttpErrorResponse, HttpEvent, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AUTH_ENDPOINTS } from '../auth/auth.constants';
import { AuthService } from '../auth/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  const mockAuthService = {
    getAccessToken: vi.fn<() => string | null>(),
    hasRefreshToken: vi.fn<() => boolean>(),
    refreshAccessToken: vi.fn(),
    clearSession: vi.fn(),
    applyAuthResult: vi.fn(),
  };

  const mockRouter = { navigateByUrl: vi.fn().mockResolvedValue(true) };

  beforeEach(() => {
    mockAuthService.getAccessToken.mockReset();
    mockAuthService.hasRefreshToken.mockReset();
    mockAuthService.refreshAccessToken.mockReset();
    mockAuthService.clearSession.mockReset();
    mockRouter.navigateByUrl.mockReset();

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
});
