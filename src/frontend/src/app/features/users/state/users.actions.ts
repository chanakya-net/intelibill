import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { ChangeMyPasswordRequest, UpdateMyProfileRequest } from '../services/user-account.service';

export type UserMutationType = 'update-profile' | 'change-password';

export const UsersActions = createActionGroup({
  source: 'Users',
  events: {
    'Update Profile Requested': props<{ payload: UpdateMyProfileRequest }>(),
    'Update Profile Succeeded': emptyProps(),
    'Update Profile Failed': props<{ errorMessage: string }>(),

    'Change Password Requested': props<{ payload: ChangeMyPasswordRequest }>(),
    'Change Password Succeeded': emptyProps(),
    'Change Password Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
