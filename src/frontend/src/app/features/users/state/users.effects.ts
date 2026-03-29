import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
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
}

function getProfileUpdateErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'This email is already used by another account.';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'This mobile number is already used by another account.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to update profile right now. Please try again.';
}

function getChangePasswordErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.InvalidCurrentPassword') {
    return 'Current password is incorrect.';
  }

  if (title === 'Auth.PasswordNotSet') {
    return 'Password is not set for this account.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to change password right now. Please try again.';
}

function getLoadShopUsersErrorMessage(error: ApiErrorPayload | undefined): string {
  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to load shop users right now. Please try again.';
}

function getAddShopUserErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Shop.UserIsNotOwner') {
    return 'Only owner can add new users for this shop.';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'This mobile number is already used by another account.';
  }

  if (title === 'Users.RoleNotSupported') {
    return 'Role must be Manager or Sales Person.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to add shop user right now. Please try again.';
}
