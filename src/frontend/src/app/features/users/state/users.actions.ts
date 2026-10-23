import { createActionGroup, emptyProps, props } from '@ngrx/store';

import {
  ChangeMyPasswordRequest,
  InviteMemberRequest,
  Membership,
  ShopInvitation,
  ShopUser,
  UpdateMembershipRequest,
  UpdateMyProfileRequest,
} from '../services/user-account.service';

export type UserMutationType =
  | 'update-profile'
  | 'change-password'
  | 'invite-member'
  | 'update-membership'
  | 'revoke-invitation';

export const UsersActions = createActionGroup({
  source: 'Users',
  events: {
    'Update Profile Requested': props<{ payload: UpdateMyProfileRequest }>(),
    'Update Profile Succeeded': emptyProps(),
    'Update Profile Failed': props<{ errorMessage: string }>(),

    'Change Password Requested': props<{ payload: ChangeMyPasswordRequest }>(),
    'Change Password Succeeded': emptyProps(),
    'Change Password Failed': props<{ errorMessage: string }>(),

    'Load Shop Users Requested': emptyProps(),
    'Load Shop Users Succeeded': props<{ users: readonly ShopUser[] }>(),
    'Load Shop Users Failed': props<{ errorMessage: string }>(),

    'Load Invitations Requested': props<{ shopId: string }>(),
    'Load Invitations Succeeded': props<{ invitations: readonly ShopInvitation[] }>(),
    'Load Invitations Failed': props<{ errorMessage: string }>(),

    'Invite Member Requested': props<{ shopId: string; payload: InviteMemberRequest }>(),
    'Invite Member Succeeded': props<{ invitation: ShopInvitation }>(),
    'Invite Member Failed': props<{ errorMessage: string }>(),

    'Revoke Invitation Requested': props<{ shopId: string; invitationId: string }>(),
    'Revoke Invitation Succeeded': props<{ invitationId: string }>(),
    'Revoke Invitation Failed': props<{ errorMessage: string }>(),

    'Update Membership Requested': props<{
      shopId: string;
      userId: string;
      payload: UpdateMembershipRequest;
    }>(),
    'Update Membership Succeeded': props<{ membership: Membership }>(),
    'Update Membership Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
