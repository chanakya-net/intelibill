import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { ShopsActions } from '../../shops/state/shops.actions';
import { UserAccountService } from '../services/user-account.service';
import { UsersActions } from './users.actions';

@Injectable()
export class UsersEffects {
  private readonly actions$ = inject(Actions);
  private readonly userAccountService = inject(UserAccountService);

  readonly updateProfile$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.updateProfileRequested),
      switchMap(({ payload }) =>
        this.userAccountService.updateMyProfile(payload).pipe(
          map(() => UsersActions.updateProfileSucceeded()),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.updateProfileFailed({
                errorMessage: getProfileUpdateErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly changePassword$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.changePasswordRequested),
      switchMap(({ payload }) =>
        this.userAccountService.changeMyPassword(payload).pipe(
          map(() => UsersActions.changePasswordSucceeded()),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.changePasswordFailed({
                errorMessage: getChangePasswordErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly loadShopUsers$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.loadShopUsersRequested),
      switchMap(() =>
        this.userAccountService.getShopUsers().pipe(
          map((users) => UsersActions.loadShopUsersSucceeded({ users })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.loadShopUsersFailed({
                errorMessage: getLoadShopUsersErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly addShopUser$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.addShopUserRequested),
      switchMap(({ payload }) =>
        this.userAccountService.addShopUser(payload).pipe(
          map((user) => UsersActions.addShopUserSucceeded({ user })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.addShopUserFailed({
                errorMessage: getAddShopUserErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly editShopUser$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.editShopUserRequested),
      switchMap(({ userId, payload }) =>
        this.userAccountService.editShopUser(userId, payload).pipe(
          map((user) => UsersActions.editShopUserSucceeded({ user })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.editShopUserFailed({
                errorMessage: getEditShopUserErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly reloadShopUsersAfterShopSwitch$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopSucceeded, ShopsActions.setDefaultShopSucceeded),
      map(() => UsersActions.loadShopUsersRequested())
    )
  );
}

function getProfileUpdateErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'errors.auth.emailAlreadyInUseByAnother';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'errors.auth.phoneAlreadyInUseByAnother';
  }

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'errors.auth.emailAlreadyInUseByAnother';
  }

  return 'errors.users.unableToUpdateProfile';
}

function getChangePasswordErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.InvalidCurrentPassword') {
    return 'errors.auth.invalidCurrentPassword';
  }

  if (title === 'Auth.PasswordNotSet') {
    return 'errors.auth.passwordNotSet';
  }

  return 'errors.users.unableToChangePassword';
}

function getLoadShopUsersErrorMessage(error: ApiErrorPayload | undefined): string {
  return 'errors.users.unableToLoadShopUsers';
}

function getAddShopUserErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Shop.UserIsNotOwner') {
    return 'errors.users.onlyOwnerCanAddUsers';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'errors.auth.phoneAlreadyInUseByAnother';
  }

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'errors.auth.emailAlreadyInUseByAnother';
  }

  if (title === 'Users.RoleNotSupported') {
    return 'errors.users.roleNotSupported';
  }

  return 'errors.users.unableToAddShopUser';
}

function getEditShopUserErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Shop.UserIsNotOwner') {
    return 'errors.users.onlyOwnerCanEditUsers';
  }

  if (title === 'Users.CannotModifyOwner') {
    return 'errors.users.ownerCannotBeModified';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'errors.auth.phoneAlreadyInUseByAnother';
  }

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'errors.auth.emailAlreadyInUseByAnother';
  }

  if (title === 'Users.RoleNotSupported') {
    return 'errors.users.roleNotSupported';
  }

  return 'errors.users.unableToUpdateUser';
}
