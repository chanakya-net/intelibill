import { createFeatureSelector, createSelector } from '@ngrx/store';

import { registerFeatureKey, RegisterState } from './register.reducer';

export const selectRegisterState = createFeatureSelector<RegisterState>(registerFeatureKey);

export const selectRegisterSubmitting = createSelector(
  selectRegisterState,
  (state) => state.submitting,
);
export const selectRegisterErrorMessage = createSelector(
  selectRegisterState,
  (state) => state.errorMessage,
);

export const selectRegisterAccepted = createSelector(
  selectRegisterState,
  (state) => state.accepted,
);
export const selectRegisterEmail = createSelector(selectRegisterState, (state) => state.email);
export const selectRegisterClaim = createSelector(selectRegisterState, (state) => state.claim);
