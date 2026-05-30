import { createReducer, on } from '@ngrx/store';

import { RegisterActions } from './register.actions';

export const registerFeatureKey = 'authRegistration';

export interface RegisterState {
  readonly submitting: boolean;
  readonly errorMessage: string;
}

const initialState: RegisterState = {
  submitting: false,
  errorMessage: '',
};

export const registerReducer = createReducer(
  initialState,
  on(RegisterActions.requested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
  })),
  on(RegisterActions.succeeded, () => ({
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
  }))
);
