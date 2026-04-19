import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AuthResult } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { USER_ENDPOINTS } from '../../../core/auth/auth.constants';
import { ChangeMyPasswordRequest, UpdateMyProfileRequest, UserAccountService } from './user-account.service';

describe('UserAccountService', () => {
  let service: UserAccountService;
  let httpMock: HttpTestingController;

  const mockAuthResult: AuthResult = {
    accessToken: 'tok',
    refreshToken: 'ref',
    accessTokenExpiresAt: new Date().toISOString(),
    refreshTokenExpiresAt: new Date().toISOString(),
    user: { id: 'u1', email: 'u@t.com', phoneNumber: null, firstName: 'A', lastName: 'B' },
    activeShopId: 'sh1',
    shops: [],
  };

  const authService = {
    applyAuthResult: vi.fn(),
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        UserAccountService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authService },
      ],
    });
    service = TestBed.inject(UserAccountService);
    httpMock = TestBed.inject(HttpTestingController);
    authService.applyAuthResult.mockReset();
  });

  afterEach(() => httpMock.verify());

  it('updateMyProfile sends PUT and calls applyAuthResult', () => {
    const payload: UpdateMyProfileRequest = { email: 'u@t.com', phoneNumber: null, firstName: 'A', lastName: 'B' };
    let completed = false;

    service.updateMyProfile(payload).subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(USER_ENDPOINTS.me);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush(mockAuthResult);

    expect(authService.applyAuthResult).toHaveBeenCalledWith(mockAuthResult);
    expect(completed).toBe(true);
  });

  it('changeMyPassword sends POST to changePassword endpoint', () => {
    const payload: ChangeMyPasswordRequest = { currentPassword: 'Old1!', newPassword: 'New1234!' };
    let completed = false;

    service.changeMyPassword(payload).subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(USER_ENDPOINTS.changePassword);
    expect(req.request.method).toBe('POST');
    req.flush(null);

    expect(completed).toBe(true);
  });

  it('getShopUsers sends GET to users list endpoint', () => {
    const users = [{ userId: 'u1', firstName: 'A', lastName: 'B', email: 'a@b.com', phoneNumber: null, role: 'Manager', isLoginEnabled: true, shopIds: [] }];

    service.getShopUsers().subscribe((res) => expect(res).toEqual(users));

    const req = httpMock.expectOne(USER_ENDPOINTS.list);
    expect(req.request.method).toBe('GET');
    req.flush(users);
  });

  it('addShopUser sends POST to users endpoint', () => {
    const payload = { shopIds: ['sh1'], email: 'u@t.com', firstName: 'A', lastName: 'B', phoneNumber: '+9198', password: 'Pass1!', confirmPassword: 'Pass1!', role: 'Manager' as const };
    const user = { userId: 'u1', ...payload, isLoginEnabled: true };

    service.addShopUser(payload).subscribe((res) => expect(res).toEqual(user));

    const req = httpMock.expectOne(USER_ENDPOINTS.add);
    expect(req.request.method).toBe('POST');
    req.flush(user);
  });

  it('editShopUser sends PUT to users/:id endpoint', () => {
    const userId = 'u1';
    const payload = { email: 'u@t.com', firstName: 'A', lastName: 'B', phoneNumber: '+9198', role: 'Manager' as const, isLoginEnabled: true };
    const user = { userId, ...payload, shopIds: [] };

    service.editShopUser(userId, payload).subscribe((res) => expect(res).toEqual(user));

    const req = httpMock.expectOne(USER_ENDPOINTS.update(userId));
    expect(req.request.method).toBe('PUT');
    req.flush(user);
  });
});
