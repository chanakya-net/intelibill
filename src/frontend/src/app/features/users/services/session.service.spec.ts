import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { DeviceSession } from '../../../core/auth/auth.models';
import { SESSION_ENDPOINTS } from '../../../core/auth/auth.constants';
import { SessionService } from './session.service';

describe('SessionService', () => {
  let service: SessionService;
  let httpMock: HttpTestingController;

  const device: DeviceSession = {
    sessionId: 's1',
    deviceLabel: null,
    userAgent: 'Chrome on Windows',
    ipAddress: '203.0.113.4',
    activeShopId: 'sh1',
    createdAt: new Date().toISOString(),
    lastSeenAt: new Date().toISOString(),
    expiresAt: new Date().toISOString(),
    isCurrent: true,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SessionService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SessionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists the devices signed in to this account', () => {
    let listed: readonly DeviceSession[] = [];

    service.getMySessions().subscribe((sessions) => (listed = sessions));

    const req = httpMock.expectOne(SESSION_ENDPOINTS.list);
    expect(req.request.method).toBe('GET');
    req.flush([device]);

    expect(listed).toEqual([device]);
  });

  it('ends one device by its session id', () => {
    let completed = false;

    service.revokeSession('s2').subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(SESSION_ENDPOINTS.revoke('s2'));
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(completed).toBe(true);
  });

  it('ends every device, this one included', () => {
    let completed = false;

    service.revokeAllSessions().subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(SESSION_ENDPOINTS.revokeAll);
    expect(req.request.method).toBe('POST');
    req.flush(null);

    expect(completed).toBe(true);
  });
});
