import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { AuthUser } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { MEMBERSHIP_ENDPOINTS, USER_ENDPOINTS } from '../../../core/auth/auth.constants';
import { ChangeMyPasswordRequest, UpdateMyProfileRequest, UserAccountService } from './user-account.service';

describe('UserAccountService', () => {
  let service: UserAccountService;
  let httpMock: HttpTestingController;

  const savedProfile: AuthUser = {
    id: 'u1',
    email: 'u@t.com',
    phoneNumber: null,
    firstName: 'A',
    lastName: 'B',
  };

  const authService = {
    applyUser: vi.fn(),
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
    authService.applyUser.mockReset();
    authService.applyAuthResult.mockReset();
  });

  afterEach(() => httpMock.verify());

  it('updateMyProfile sends PUT and adopts the saved profile without a new token', () => {
    const payload: UpdateMyProfileRequest = { email: 'u@t.com', phoneNumber: null, firstName: 'A', lastName: 'B' };
    let completed = false;

    service.updateMyProfile(payload).subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(USER_ENDPOINTS.me);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush(savedProfile);

    // Saving a name is not a credential event, so there is no auth result to adopt.
    expect(authService.applyUser).toHaveBeenCalledWith(savedProfile);
    expect(authService.applyAuthResult).not.toHaveBeenCalled();
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
    const users = [
      {
        userId: 'u1',
        firstName: 'A',
        lastName: 'B',
        email: 'a@b.com',
        phoneNumber: null,
        role: 'Manager',
        status: 'Active',
        membershipVersion: 2,
        shopIds: [],
      },
    ];

    service.getShopUsers().subscribe((res) => expect(res).toEqual(users));

    const req = httpMock.expectOne(USER_ENDPOINTS.list);
    expect(req.request.method).toBe('GET');
    req.flush(users);
  });

  it('editMemberNeverSendsGlobalIdentity', () => {
    const membership = {
      userId: 'u1',
      shopId: 'sh1',
      displayName: 'A B',
      role: 'Manager',
      status: 'Active',
      version: 3,
    };

    service
      .updateMembership('sh1', 'u1', { role: 'Manager', status: 'Active', expectedVersion: 2 })
      .subscribe((res) => expect(res).toEqual(membership));

    const req = httpMock.expectOne(MEMBERSHIP_ENDPOINTS.update('sh1', 'u1'));
    expect(req.request.method).toBe('PATCH');
    // Role, status and the version read with the row — and nothing that belongs to the account.
    expect(req.request.body).toEqual({ role: 'Manager', status: 'Active', expectedVersion: 2 });
    expect(Object.keys(req.request.body as object).sort()).toEqual([
      'expectedVersion',
      'role',
      'status',
    ]);
    req.flush(membership);
  });

  it('surfaces a membership version conflict instead of retrying the change', () => {
    let conflictCode = '';

    service
      .updateMembership('sh1', 'u1', { role: 'Staff', status: 'Removed', expectedVersion: 1 })
      .subscribe({
        next: () => expect.unreachable('a stale version must not succeed'),
        error: (response: { status: number; error: { code: string } }) => {
          expect(response.status).toBe(409);
          conflictCode = response.error.code;
        },
      });

    httpMock
      .expectOne(MEMBERSHIP_ENDPOINTS.update('sh1', 'u1'))
      .flush(
        { code: 'Users.MembershipVersionConflict', description: 'Reload and try again.' },
        { status: 409, statusText: 'Conflict' },
      );

    expect(conflictCode).toBe('Users.MembershipVersionConflict');
  });

  it('inviteMember posts only the address and the role', () => {
    const invitation = {
      invitationId: 'i1',
      shopId: 'sh1',
      email: 'invited@test.com',
      role: 'Manager',
      status: 'Pending',
      expiresAt: '2026-11-01T00:00:00Z',
      createdAt: '2026-10-25T00:00:00Z',
    };

    service
      .inviteMember('sh1', { email: 'invited@test.com', role: 'Manager' })
      .subscribe((res) => expect(res).toEqual(invitation));

    const req = httpMock.expectOne(MEMBERSHIP_ENDPOINTS.invitations('sh1'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'invited@test.com', role: 'Manager' });
    req.flush(invitation);
  });

  it('getInvitations sends GET to the shop invitations endpoint', () => {
    service.getInvitations('sh1').subscribe((res) => expect(res).toEqual([]));

    const req = httpMock.expectOne(MEMBERSHIP_ENDPOINTS.invitations('sh1'));
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('revokeInvitation sends DELETE for one invitation', () => {
    let completed = false;

    service.revokeInvitation('sh1', 'i1').subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(MEMBERSHIP_ENDPOINTS.invitation('sh1', 'i1'));
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(completed).toBe(true);
  });

  it('acceptInvitation sends the token in the body, never in the URL', () => {
    const membership = {
      userId: 'u9',
      shopId: 'sh1',
      displayName: 'Invited Person',
      role: 'Manager',
      status: 'Active',
      version: 0,
    };

    service.acceptInvitation('i1', 'SECRET-TOKEN').subscribe((res) => expect(res).toEqual(membership));

    const req = httpMock.expectOne(MEMBERSHIP_ENDPOINTS.acceptInvitation('i1'));
    expect(req.request.method).toBe('POST');
    expect(req.request.url).not.toContain('SECRET-TOKEN');
    expect(req.request.body).toEqual({ token: 'SECRET-TOKEN' });
    req.flush(membership);
  });
});
