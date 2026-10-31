import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { UserAccountService } from '../services/user-account.service';
import { ShopsActions } from '../../shops/state/shops.actions';
import { UsersActions } from './users.actions';
import { UsersEffects } from './users.effects';

describe('UsersEffects', () => {
  let actions$: Subject<Action>;
  let effects: UsersEffects;

  const userAccountService = {
    updateMyProfile: vi.fn<UserAccountService['updateMyProfile']>(),
    changeMyPassword: vi.fn<UserAccountService['changeMyPassword']>(),
    getShopUsers: vi.fn<UserAccountService['getShopUsers']>(),
    getInvitations: vi.fn<UserAccountService['getInvitations']>(),
    inviteMember: vi.fn<UserAccountService['inviteMember']>(),
    revokeInvitation: vi.fn<UserAccountService['revokeInvitation']>(),
    updateMembership: vi.fn<UserAccountService['updateMembership']>(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    userAccountService.updateMyProfile.mockReset();
    userAccountService.changeMyPassword.mockReset();
    userAccountService.getShopUsers.mockReset();
    userAccountService.getInvitations.mockReset();
    userAccountService.inviteMember.mockReset();
    userAccountService.revokeInvitation.mockReset();
    userAccountService.updateMembership.mockReset();

    TestBed.configureTestingModule({
      providers: [
        UsersEffects,
        { provide: UserAccountService, useValue: userAccountService },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(UsersEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches updateProfileSucceeded on profile update success', async () => {
    userAccountService.updateMyProfile.mockReturnValue(of(void 0));

    const output = firstValueFrom(effects.updateProfile$.pipe(take(1)));

    actions$.next(
      UsersActions.updateProfileRequested({
        payload: {
          firstName: 'Jane',
          lastName: 'Doe',
          email: 'jane@example.com',
          phoneNumber: null,
        },
      }),
    );

    await expect(output).resolves.toEqual(UsersActions.updateProfileSucceeded());
  });

  it('maps email conflict into updateProfileFailed message', async () => {
    userAccountService.updateMyProfile.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.EmailAlreadyInUse' } })),
    );

    const output = firstValueFrom(effects.updateProfile$.pipe(take(1)));

    actions$.next(
      UsersActions.updateProfileRequested({
        payload: {
          firstName: 'Jane',
          lastName: 'Doe',
          email: 'jane@example.com',
          phoneNumber: null,
        },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.updateProfileFailed({
        errorMessage: 'errors.auth.emailAlreadyInUseByAnother',
      }),
    );
  });

  it('maps API detail into updateProfileFailed message', async () => {
    userAccountService.updateMyProfile.mockReturnValue(
      throwError(() => ({ error: { title: 'Unknown', detail: 'Profile update failed.' } })),
    );

    const output = firstValueFrom(effects.updateProfile$.pipe(take(1)));

    actions$.next(
      UsersActions.updateProfileRequested({
        payload: {
          firstName: 'Jane',
          lastName: 'Doe',
          email: 'jane@example.com',
          phoneNumber: null,
        },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.updateProfileFailed({
        errorMessage: 'errors.users.unableToUpdateProfile',
      }),
    );
  });

  it('dispatches changePasswordSucceeded on success', async () => {
    userAccountService.changeMyPassword.mockReturnValue(of(void 0));

    const output = firstValueFrom(effects.changePassword$.pipe(take(1)));

    actions$.next(
      UsersActions.changePasswordRequested({
        payload: {
          currentPassword: 'OldPass123!',
          newPassword: 'NewPass123!',
        },
      }),
    );

    await expect(output).resolves.toEqual(UsersActions.changePasswordSucceeded());
  });

  it('maps invalid current password into changePasswordFailed message', async () => {
    userAccountService.changeMyPassword.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.InvalidCurrentPassword' } })),
    );

    const output = firstValueFrom(effects.changePassword$.pipe(take(1)));

    actions$.next(
      UsersActions.changePasswordRequested({
        payload: {
          currentPassword: 'OldPass123!',
          newPassword: 'NewPass123!',
        },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.changePasswordFailed({
        errorMessage: 'errors.auth.invalidCurrentPassword',
      }),
    );
  });

  it('maps fallback message when password API error has no detail', async () => {
    userAccountService.changeMyPassword.mockReturnValue(
      throwError(() => ({ error: { title: 'Unknown' } })),
    );

    const output = firstValueFrom(effects.changePassword$.pipe(take(1)));

    actions$.next(
      UsersActions.changePasswordRequested({
        payload: {
          currentPassword: 'OldPass123!',
          newPassword: 'NewPass123!',
        },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.changePasswordFailed({
        errorMessage: 'errors.users.unableToChangePassword',
      }),
    );
  });

  it('dispatches loadShopUsersSucceeded on users load success', async () => {
    userAccountService.getShopUsers.mockReturnValue(
      of([
        {
          userId: 'u1',
          firstName: 'Owner',
          lastName: 'User',
          email: 'owner@test.com',
          phoneNumber: '+15551234567',
          role: 'Owner',
          status: 'Active',
          membershipVersion: 0,
          shopIds: ['shop-1'],
        },
      ]),
    );

    const output = firstValueFrom(effects.loadShopUsers$.pipe(take(1)));

    actions$.next(UsersActions.loadShopUsersRequested());

    await expect(output).resolves.toEqual(
      UsersActions.loadShopUsersSucceeded({
        users: [
          {
            userId: 'u1',
            firstName: 'Owner',
            lastName: 'User',
            email: 'owner@test.com',
            phoneNumber: '+15551234567',
            role: 'Owner',
            status: 'Active',
            membershipVersion: 0,
            shopIds: ['shop-1'],
          },
        ],
      }),
    );
  });

  it('maps owner-only failure when an invitation is refused', async () => {
    userAccountService.inviteMember.mockReturnValue(
      throwError(() => ({ error: { title: 'Shop.UserIsNotOwner' } })),
    );

    const output = firstValueFrom(effects.inviteMember$.pipe(take(1)));

    actions$.next(
      UsersActions.inviteMemberRequested({
        shopId: 'shop-1',
        payload: { email: 'sales@shop.com', role: 'Staff' },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.inviteMemberFailed({ errorMessage: 'errors.users.onlyOwnerCanAddUsers' }),
    );
  });

  it('explains an address that already has live access', async () => {
    userAccountService.inviteMember.mockReturnValue(
      throwError(() => ({ error: { code: 'Users.InvitationAlreadyMember' } })),
    );

    const output = firstValueFrom(effects.inviteMember$.pipe(take(1)));

    actions$.next(
      UsersActions.inviteMemberRequested({
        shopId: 'shop-1',
        payload: { email: 'sales@shop.com', role: 'Manager' },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.inviteMemberFailed({ errorMessage: 'errors.users.invitationAlreadyMember' }),
    );
  });

  it('surfaces a membership version conflict as its own message', async () => {
    userAccountService.updateMembership.mockReturnValue(
      throwError(() => ({ error: { code: 'Users.MembershipVersionConflict' } })),
    );

    const output = firstValueFrom(effects.updateMembership$.pipe(take(1)));

    actions$.next(
      UsersActions.updateMembershipRequested({
        shopId: 'shop-1',
        userId: 'u1',
        payload: { role: 'Manager', status: 'Active', expectedVersion: 1 },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.updateMembershipFailed({
        errorMessage: 'errors.users.membershipVersionConflict',
      }),
    );
  });

  it('dispatches updateMembershipSucceeded with the new authority', async () => {
    const membership = {
      userId: 'u1',
      shopId: 'shop-1',
      displayName: 'Sales Rep',
      role: 'Manager',
      status: 'Active',
      version: 2,
    };
    userAccountService.updateMembership.mockReturnValue(of(membership));

    const output = firstValueFrom(effects.updateMembership$.pipe(take(1)));

    actions$.next(
      UsersActions.updateMembershipRequested({
        shopId: 'shop-1',
        userId: 'u1',
        payload: { role: 'Manager', status: 'Active', expectedVersion: 1 },
      }),
    );

    await expect(output).resolves.toEqual(UsersActions.updateMembershipSucceeded({ membership }));
  });

  it('re-reads invitations after a membership change, because a change can void one', async () => {
    const output = firstValueFrom(effects.reloadInvitationsAfterMembershipChange$.pipe(take(1)));

    actions$.next(
      UsersActions.updateMembershipSucceeded({
        membership: {
          userId: 'u1',
          shopId: 'shop-1',
          displayName: 'Sales Rep',
          role: 'Staff',
          status: 'Suspended',
          version: 3,
        },
      }),
    );

    await expect(output).resolves.toEqual(
      UsersActions.loadInvitationsRequested({ shopId: 'shop-1' }),
    );
  });

  it('reloads shop users after default shop switch succeeds', async () => {
    const output = firstValueFrom(effects.reloadShopUsersAfterShopSwitch$.pipe(take(1)));

    actions$.next(ShopsActions.setDefaultShopSucceeded());

    await expect(output).resolves.toEqual(UsersActions.loadShopUsersRequested());
  });

  it('reloads shop users after create shop succeeds', async () => {
    const output = firstValueFrom(effects.reloadShopUsersAfterShopSwitch$.pipe(take(1)));

    actions$.next(ShopsActions.createShopSucceeded());

    await expect(output).resolves.toEqual(UsersActions.loadShopUsersRequested());
  });
});
