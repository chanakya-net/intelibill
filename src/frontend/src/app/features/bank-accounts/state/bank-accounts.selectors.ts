import { createSelector } from '@ngrx/store';
import { bankAccountsAdapter, bankAccountsFeature } from './bank-accounts.reducer';

export const selectBankAccountsState = bankAccountsFeature.selectBankAccountsState;
const bankAccountEntitySelectors = bankAccountsAdapter.getSelectors(selectBankAccountsState);

export const selectBankAccounts = bankAccountEntitySelectors.selectAll;
export const selectBankAccountEntities = bankAccountEntitySelectors.selectEntities;
export const selectBankAccountsLoading = createSelector(selectBankAccountsState, (state) => state.loading);
export const selectBankAccountsSubmitting = createSelector(selectBankAccountsState, (state) => state.submitting);
export const selectBankAccountsErrorMessage = createSelector(selectBankAccountsState, (state) => state.errorMessage);
export const selectBankAccountsLastMutationSucceeded = createSelector(
  selectBankAccountsState,
  (state) => state.lastMutationSucceeded
);
