import { HttpEvent, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { vi } from 'vitest';

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
});
