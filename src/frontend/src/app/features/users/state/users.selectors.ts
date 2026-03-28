import { createSelector } from '@ngrx/store';

import { usersFeature } from './users.reducer';

export const selectUsersState = usersFeature.selectUsersState;

export const selectUsersSubmitting = createSelector(selectUsersState, (state) => state.submitting);
export const selectUsersErrorMessage = createSelector(selectUsersState, (state) => state.errorMessage);
export const selectUsersLastMutationType = createSelector(selectUsersState, (state) => state.lastMutationType);
export const selectUsersLastMutationSucceeded = createSelector(
  selectUsersState,
  (state) => state.lastMutationSucceeded
);
