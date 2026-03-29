import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { AddShopUserRequest, ChangeMyPasswordRequest, ShopUser, UpdateMyProfileRequest } from '../services/user-account.service';

export type UserMutationType = 'update-profile' | 'change-password' | 'add-shop-user';

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

    'Add Shop User Requested': props<{ payload: AddShopUserRequest }>(),
    'Add Shop User Succeeded': props<{ user: ShopUser }>(),
    'Add Shop User Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
