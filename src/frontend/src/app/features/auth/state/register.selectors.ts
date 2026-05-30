import { createFeatureSelector, createSelector } from '@ngrx/store';

import { registerFeatureKey, RegisterState } from './register.reducer';

export const selectRegisterState = createFeatureSelector<RegisterState>(registerFeatureKey);

export const selectRegisterSubmitting = createSelector(selectRegisterState, (state) => state.submitting);
export const selectRegisterErrorMessage = createSelector(selectRegisterState, (state) => state.errorMessage);
