import { createFeature, createReducer, on } from '@ngrx/store';

import { UserMutationType, UsersActions } from './users.actions';

export const usersFeatureKey = 'users';

export interface UsersState {
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationType: UserMutationType | null;
  readonly lastMutationSucceeded: boolean;
}

const initialState: UsersState = {
  submitting: false,
  errorMessage: '',
  lastMutationType: null,
  lastMutationSucceeded: false,
};

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
