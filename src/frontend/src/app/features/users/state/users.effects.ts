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
          catchError(() =>
            of(
              UsersActions.loadShopUsersFailed({
                errorMessage: 'errors.users.unableToLoadShopUsers',
              })
            )
          )
        )
      )
    )
  );

  readonly loadInvitations$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.loadInvitationsRequested),
      switchMap(({ shopId }) =>
        this.userAccountService.getInvitations(shopId).pipe(
          map((invitations) => UsersActions.loadInvitationsSucceeded({ invitations })),
          catchError(() =>
            of(
              UsersActions.loadInvitationsFailed({
                errorMessage: 'errors.users.unableToLoadInvitations',
              })
            )
          )
        )
      )
    )
  );

  readonly inviteMember$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.inviteMemberRequested),
      switchMap(({ shopId, payload }) =>
        this.userAccountService.inviteMember(shopId, payload).pipe(
          map((invitation) => UsersActions.inviteMemberSucceeded({ invitation })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.inviteMemberFailed({
                errorMessage: getInviteMemberErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly revokeInvitation$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.revokeInvitationRequested),
      switchMap(({ shopId, invitationId }) =>
        this.userAccountService.revokeInvitation(shopId, invitationId).pipe(
          map(() => UsersActions.revokeInvitationSucceeded({ invitationId })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.revokeInvitationFailed({
                errorMessage: getInviteMemberErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly updateMembership$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.updateMembershipRequested),
      switchMap(({ shopId, userId, payload }) =>
        this.userAccountService.updateMembership(shopId, userId, payload).pipe(
          map((membership) => UsersActions.updateMembershipSucceeded({ membership })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              UsersActions.updateMembershipFailed({
                errorMessage: getUpdateMembershipErrorMessage(error.error),
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

  /** A membership change can void an outstanding invitation, so the list is re-read. */
  readonly reloadInvitationsAfterMembershipChange$ = createEffect(() =>
    this.actions$.pipe(
      ofType(UsersActions.updateMembershipSucceeded),
      map(({ membership }) => UsersActions.loadInvitationsRequested({ shopId: membership.shopId }))
    )
  );
}

function getProfileUpdateErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.code ?? error?.title ?? '';
  if (title === 'Auth.EmailChangeRequired') return 'identity.emailChangeRequired';
  if (title === 'Auth.InvitationRequired') return 'identity.invitationRequired';

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'errors.auth.emailAlreadyInUseByAnother';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'errors.auth.phoneAlreadyInUseByAnother';
  }

  return 'errors.users.unableToUpdateProfile';
}

function getChangePasswordErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.code ?? error?.title ?? '';
  if (title === 'Auth.EmailChangeRequired') return 'identity.emailChangeRequired';
  if (title === 'Auth.InvitationRequired') return 'identity.invitationRequired';

  if (title === 'Auth.InvalidCurrentPassword') {
    return 'errors.auth.invalidCurrentPassword';
  }

  if (title === 'Auth.PasswordNotSet') {
    return 'errors.auth.passwordNotSet';
  }

  return 'errors.users.unableToChangePassword';
}

function getInviteMemberErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.code ?? error?.title ?? '';

  switch (title) {
    case 'Shop.UserIsNotOwner':
      return 'errors.users.onlyOwnerCanAddUsers';
    case 'Users.RoleNotSupported':
      return 'errors.users.roleNotSupported';
    case 'Users.InvitationAlreadyMember':
      return 'errors.users.invitationAlreadyMember';
    case 'Users.InvitationBudgetExhausted':
      return 'errors.users.invitationBudgetExhausted';
    case 'Users.InvitationDeliveryUnavailable':
      return 'errors.users.invitationDeliveryUnavailable';
    case 'Users.InvitationNotAcceptable':
      return 'errors.users.invitationNotAcceptable';
    case 'Users.InvitationNotFound':
      return 'errors.users.invitationNotFound';
    default:
      return 'errors.users.unableToInviteMember';
  }
}

function getUpdateMembershipErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.code ?? error?.title ?? '';

  switch (title) {
    case 'Users.MembershipVersionConflict':
      return 'errors.users.membershipVersionConflict';
    case 'Users.MembershipOperationRequired':
      return 'errors.users.membershipOperationRequired';
    case 'Users.CannotModifyOwner':
      return 'errors.users.ownerCannotBeModified';
    case 'Users.MembershipNotFound':
      return 'errors.users.membershipNotFound';
    case 'Users.MembershipStatusNotSupported':
      return 'errors.users.membershipStatusNotSupported';
    case 'Users.RoleNotSupported':
      return 'errors.users.roleNotSupported';
    case 'Shop.UserIsNotOwner':
      return 'errors.users.onlyOwnerCanEditUsers';
    default:
      return 'errors.users.unableToUpdateUser';
  }
}
