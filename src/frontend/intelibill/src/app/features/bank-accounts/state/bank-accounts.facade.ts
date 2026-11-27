import { Injectable, Signal, inject } from '@angular/core';
import { Store } from '@ngrx/store';
import { AddBankAccountRequest, BankAccount, UpdateBankAccountRequest } from '../services/bank-account.service';
import { BankAccountsActions } from './bank-accounts.actions';
import {
  selectBankAccounts,
  selectBankAccountsErrorMessage,
  selectBankAccountsLastMutationSucceeded,
  selectBankAccountsLoading,
  selectBankAccountsSubmitting,
} from './bank-accounts.selectors';

@Injectable({ providedIn: 'root' })
export class BankAccountsFacade {
  private readonly store = inject(Store);

  readonly bankAccounts: Signal<readonly BankAccount[]> = this.store.selectSignal(selectBankAccounts);
  readonly isLoading: Signal<boolean> = this.store.selectSignal(selectBankAccountsLoading);
  readonly isSubmitting: Signal<boolean> = this.store.selectSignal(selectBankAccountsSubmitting);
  readonly errorMessage: Signal<string> = this.store.selectSignal(selectBankAccountsErrorMessage);
  readonly lastMutationSucceeded: Signal<boolean> = this.store.selectSignal(selectBankAccountsLastMutationSucceeded);

  load(): void {
    this.store.dispatch(BankAccountsActions.loadBankAccountsRequested());
  }

  addBankAccount(payload: AddBankAccountRequest): void {
    this.store.dispatch(BankAccountsActions.addBankAccountRequested({ payload }));
  }

  updateBankAccount(id: string, payload: UpdateBankAccountRequest): void {
    this.store.dispatch(BankAccountsActions.updateBankAccountRequested({ id, payload }));
  }

  deleteBankAccount(id: string): void {
    this.store.dispatch(BankAccountsActions.deleteBankAccountRequested({ id }));
  }

  clearError(): void {
    this.store.dispatch(BankAccountsActions.clearError());
  }

  clearMutationStatus(): void {
    this.store.dispatch(BankAccountsActions.clearMutationStatus());
  }
}
