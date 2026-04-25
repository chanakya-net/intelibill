import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { AddBankAccountRequest, BankAccount, UpdateBankAccountRequest } from '../services/bank-account.service';

export const BankAccountsActions = createActionGroup({
  source: 'Bank Accounts',
  events: {
    'Load Bank Accounts Requested': emptyProps(),
    'Load Bank Accounts Succeeded': props<{ bankAccounts: readonly BankAccount[] }>(),
    'Load Bank Accounts Failed': props<{ errorMessage: string }>(),

    'Add Bank Account Requested': props<{ payload: AddBankAccountRequest }>(),
    'Add Bank Account Succeeded': props<{ bankAccount: BankAccount }>(),
    'Add Bank Account Failed': props<{ errorMessage: string }>(),

    'Update Bank Account Requested': props<{ id: string; payload: UpdateBankAccountRequest }>(),
    'Update Bank Account Succeeded': props<{ bankAccount: BankAccount }>(),
    'Update Bank Account Failed': props<{ errorMessage: string }>(),

    'Delete Bank Account Requested': props<{ id: string }>(),
    'Delete Bank Account Succeeded': props<{ id: string }>(),
    'Delete Bank Account Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
  },
});
