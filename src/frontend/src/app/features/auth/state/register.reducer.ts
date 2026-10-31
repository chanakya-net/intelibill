import { createReducer, on } from '@ngrx/store';

import { RegisterActions } from './register.actions';

export const registerFeatureKey = 'authRegistration';

export interface RegisterState {
  readonly submitting: boolean;
  readonly email: string;
  readonly accepted: boolean;
  readonly claim: boolean;
  readonly errorMessage: string;
}

const initialState: RegisterState = {
  submitting: false,
  email: '',
  accepted: false,
  claim: false,
  errorMessage: '',
};

export const registerReducer = createReducer(
  initialState,
  on(RegisterActions.reset, () => initialState),
  on(RegisterActions.requested, (state, { email, claim }) => ({
    ...state,
    submitting: true,
    email,
    claim: claim ?? false,
    errorMessage: '',
  })),
  on(RegisterActions.succeeded, (state) => ({
    ...state,
    accepted: true,
    submitting: false,
    errorMessage: '',
  })),
  on(RegisterActions.failed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
  })),
  on(RegisterActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
);
