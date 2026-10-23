import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';

import { ShopInvitation, ShopUser } from '../services/user-account.service';
import { UserMutationType, UsersActions } from './users.actions';

export const usersFeatureKey = 'users';

export const usersAdapter = createEntityAdapter<ShopUser>({
  selectId: (user) => user.userId,
});

export interface UsersState extends EntityState<ShopUser> {
  readonly loadingShopUsers: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: UserMutationType | null;
  readonly lastMutationSucceeded: boolean;
  readonly invitations: readonly ShopInvitation[];
}

const initialState: UsersState = usersAdapter.getInitialState({
  loadingShopUsers: false,
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
  invitations: [] as readonly ShopInvitation[],
});

export const usersReducer = createReducer(
  initialState,
  on(UsersActions.updateProfileRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-profile',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.updateProfileSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'update-profile',
    lastMutationSucceeded: true,
  })),
  on(UsersActions.updateProfileFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'update-profile',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.changePasswordRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'change-password',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.changePasswordSucceeded, (state) => ({
    ...state,
    submitting: false,
    errorMessage: '',
    lastMutationType: 'change-password',
    lastMutationSucceeded: true,
  })),
  on(UsersActions.changePasswordFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'change-password',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.loadShopUsersRequested, (state) => ({
    ...state,
    loadingShopUsers: true,
    errorMessage: '',
  })),
  on(UsersActions.loadShopUsersSucceeded, (state, { users }) =>
    usersAdapter.setAll([...users], {
      ...state,
      loadingShopUsers: false,
      errorMessage: '',
    })
  ),
  on(UsersActions.loadShopUsersFailed, (state, { errorMessage }) => ({
    ...state,
    loadingShopUsers: false,
    errorMessage,
  })),

  on(UsersActions.loadInvitationsSucceeded, (state, { invitations }) => ({
    ...state,
    invitations: [...invitations],
  })),
  on(UsersActions.loadInvitationsFailed, (state, { errorMessage }) => ({
    ...state,
    errorMessage,
  })),

  on(UsersActions.inviteMemberRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'invite-member',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.inviteMemberSucceeded, (state, { invitation }) => ({
    ...state,
    // A resend supersedes the earlier offer, so the list holds one entry per address.
    invitations: [
      invitation,
      ...state.invitations.filter(
        (existing) =>
          existing.invitationId !== invitation.invitationId &&
          existing.email.toLowerCase() !== invitation.email.toLowerCase()
      ),
    ],
    submitting: false,
    errorMessage: '',
    lastMutationType: 'invite-member',
    lastMutationSucceeded: true,
  })),
  on(UsersActions.inviteMemberFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'invite-member',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.revokeInvitationRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'revoke-invitation',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.revokeInvitationSucceeded, (state, { invitationId }) => ({
    ...state,
    invitations: state.invitations.map((invitation) =>
      invitation.invitationId === invitationId
        ? { ...invitation, status: 'Revoked' }
        : invitation
    ),
    submitting: false,
    errorMessage: '',
    lastMutationType: 'revoke-invitation',
    lastMutationSucceeded: true,
  })),
  on(UsersActions.revokeInvitationFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'revoke-invitation',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.updateMembershipRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationType: 'update-membership',
    lastMutationSucceeded: false,
  })),
  on(UsersActions.updateMembershipSucceeded, (state, { membership }) => {
    const next = {
      ...state,
      submitting: false,
      errorMessage: '',
      lastMutationType: 'update-membership' as UserMutationType,
      lastMutationSucceeded: true,
    };

    // A removed membership leaves the directory; anything else keeps its row with the new
    // authority and the version to send with the next change.
    return membership.status === 'Removed'
      ? usersAdapter.removeOne(membership.userId, next)
      : usersAdapter.updateOne(
          {
            id: membership.userId,
            changes: {
              role: membership.role,
              status: membership.status,
              membershipVersion: membership.version,
            },
          },
          next
        );
  }),
  on(UsersActions.updateMembershipFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationType: 'update-membership',
    lastMutationSucceeded: false,
  })),

  on(UsersActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(UsersActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationType: null,
    lastMutationSucceeded: false,
  }))
);

export const usersFeature = createFeature({
  name: usersFeatureKey,
  reducer: usersReducer,
});
