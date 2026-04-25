import { createEntityAdapter, EntityState } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';
import { BankAccount } from '../services/bank-account.service';
import { BankAccountsActions } from './bank-accounts.actions';

export const bankAccountsFeatureKey = 'bankAccounts';

export const bankAccountsAdapter = createEntityAdapter<BankAccount>({
  selectId: (bankAccount) => bankAccount.id,
  sortComparer: (left, right) => left.bankName.localeCompare(right.bankName),
});

export interface BankAccountsState extends EntityState<BankAccount> {
  readonly loading: boolean;
  readonly submitting: boolean;
  readonly errorMessage: string;
  readonly lastMutationSucceeded: boolean;
}

const initialState: BankAccountsState = bankAccountsAdapter.getInitialState({
  loading: false,
  submitting: false,
  errorMessage: '',
  lastMutationSucceeded: false,
});

export const bankAccountsReducer = createReducer(
  initialState,
  on(BankAccountsActions.loadBankAccountsRequested, (state) => ({
    ...state,
    loading: true,
    errorMessage: '',
  })),
  on(BankAccountsActions.loadBankAccountsSucceeded, (state, { bankAccounts }) =>
    bankAccountsAdapter.setAll([...bankAccounts], {
      ...state,
      loading: false,
      errorMessage: '',
    })
  ),
  on(BankAccountsActions.loadBankAccountsFailed, (state, { errorMessage }) => ({
    ...state,
    loading: false,
    errorMessage,
  })),

  on(BankAccountsActions.addBankAccountRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationSucceeded: false,
  })),
  on(BankAccountsActions.addBankAccountSucceeded, (state, { bankAccount }) =>
    bankAccountsAdapter.addOne(bankAccount, {
      ...state,
      submitting: false,
      errorMessage: '',
      lastMutationSucceeded: true,
    })
  ),
  on(BankAccountsActions.addBankAccountFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationSucceeded: false,
  })),

  on(BankAccountsActions.updateBankAccountRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationSucceeded: false,
  })),
  on(BankAccountsActions.updateBankAccountSucceeded, (state, { bankAccount }) =>
    bankAccountsAdapter.updateOne(
      { id: bankAccount.id, changes: bankAccount },
      {
        ...state,
        submitting: false,
        errorMessage: '',
        lastMutationSucceeded: true,
      }
    )
  ),
  on(BankAccountsActions.updateBankAccountFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationSucceeded: false,
  })),

  on(BankAccountsActions.deleteBankAccountRequested, (state) => ({
    ...state,
    submitting: true,
    errorMessage: '',
    lastMutationSucceeded: false,
  })),
  on(BankAccountsActions.deleteBankAccountSucceeded, (state, { id }) =>
    bankAccountsAdapter.removeOne(id, {
      ...state,
      submitting: false,
      errorMessage: '',
      lastMutationSucceeded: true,
    })
  ),
  on(BankAccountsActions.deleteBankAccountFailed, (state, { errorMessage }) => ({
    ...state,
    submitting: false,
    errorMessage,
    lastMutationSucceeded: false,
  })),

  on(BankAccountsActions.clearError, (state) => ({
    ...state,
    errorMessage: '',
  })),
  on(BankAccountsActions.clearMutationStatus, (state) => ({
    ...state,
    lastMutationSucceeded: false,
  }))
);

export const bankAccountsFeature = createFeature({
  name: bankAccountsFeatureKey,
  reducer: bankAccountsReducer,
});
