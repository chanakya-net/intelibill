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
    addShopUser: vi.fn<UserAccountService['addShopUser']>(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    userAccountService.updateMyProfile.mockReset();
    userAccountService.changeMyPassword.mockReset();
    userAccountService.getShopUsers.mockReset();
    userAccountService.addShopUser.mockReset();

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
      })
    );

    await expect(output).resolves.toEqual(UsersActions.updateProfileSucceeded());
  });

  it('maps email conflict into updateProfileFailed message', async () => {
    userAccountService.updateMyProfile.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.EmailAlreadyInUse' } }))
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
      })
    );

    await expect(output).resolves.toEqual(
      UsersActions.updateProfileFailed({
        errorMessage: 'This email is already used by another account.',
      })
    );
  });

  it('maps API detail into updateProfileFailed message', async () => {
    userAccountService.updateMyProfile.mockReturnValue(
      throwError(() => ({ error: { title: 'Unknown', detail: 'Profile update failed.' } }))
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
      })
    );

    await expect(output).resolves.toEqual(
      UsersActions.updateProfileFailed({
        errorMessage: 'Profile update failed.',
      })
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
      })
    );

    await expect(output).resolves.toEqual(UsersActions.changePasswordSucceeded());
  });

  it('maps invalid current password into changePasswordFailed message', async () => {
    userAccountService.changeMyPassword.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.InvalidCurrentPassword' } }))
    );

    const output = firstValueFrom(effects.changePassword$.pipe(take(1)));

    actions$.next(
      UsersActions.changePasswordRequested({
        payload: {
          currentPassword: 'OldPass123!',
          newPassword: 'NewPass123!',
        },
      })
    );

    await expect(output).resolves.toEqual(
      UsersActions.changePasswordFailed({
        errorMessage: 'Current password is incorrect.',
      })
    );
  });

  it('maps fallback message when password API error has no detail', async () => {
    userAccountService.changeMyPassword.mockReturnValue(
      throwError(() => ({ error: { title: 'Unknown' } }))
    );

    const output = firstValueFrom(effects.changePassword$.pipe(take(1)));

    actions$.next(
      UsersActions.changePasswordRequested({
        payload: {
          currentPassword: 'OldPass123!',
          newPassword: 'NewPass123!',
        },
      })
    );

    await expect(output).resolves.toEqual(
      UsersActions.changePasswordFailed({
        errorMessage: 'Unable to change password right now. Please try again.',
      })
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
        },
      ])
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
          },
        ],
      })
    );
  });

  it('maps owner-only failure on add user effect', async () => {
    userAccountService.addShopUser.mockReturnValue(
      throwError(() => ({ error: { title: 'Shop.UserIsNotOwner' } }))
    );

    const output = firstValueFrom(effects.addShopUser$.pipe(take(1)));

    actions$.next(
      UsersActions.addShopUserRequested({
        payload: {
          firstName: 'Sales',
          lastName: 'Rep',
          phoneNumber: '+15557654321',
          password: 'Pass1234!',
          confirmPassword: 'Pass1234!',
          role: 'SalesPerson',
        },
      })
    );

    await expect(output).resolves.toEqual(
      UsersActions.addShopUserFailed({
        errorMessage: 'Only owner can add new users for this shop.',
      })
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
