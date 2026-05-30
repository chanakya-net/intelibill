import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';
import { BankAccountService } from '../services/bank-account.service';
import { BankAccountsActions } from './bank-accounts.actions';
import { ShopsActions } from '../../shops/state/shops.actions';

@Injectable()
export class BankAccountsEffects {
  private readonly actions$ = inject(Actions);
  private readonly bankAccountService = inject(BankAccountService);

  readonly loadBankAccounts$ = createEffect(() =>
    this.actions$.pipe(
      ofType(BankAccountsActions.loadBankAccountsRequested),
      switchMap(() =>
        this.bankAccountService.getBankAccounts().pipe(
          map((bankAccounts) => BankAccountsActions.loadBankAccountsSucceeded({ bankAccounts })),
          catchError(() =>
            of(
              BankAccountsActions.loadBankAccountsFailed({
                errorMessage: 'errors.bankAccounts.unableToLoad',
              })
            )
          )
        )
      )
    )
  );

  readonly addBankAccount$ = createEffect(() =>
    this.actions$.pipe(
      ofType(BankAccountsActions.addBankAccountRequested),
      switchMap(({ payload }) =>
        this.bankAccountService.addBankAccount(payload).pipe(
          map((bankAccount) => BankAccountsActions.addBankAccountSucceeded({ bankAccount })),
          catchError(() =>
            of(
              BankAccountsActions.addBankAccountFailed({
                errorMessage: 'errors.bankAccounts.unableToAdd',
              })
            )
          )
        )
      )
    )
  );

  readonly updateBankAccount$ = createEffect(() =>
    this.actions$.pipe(
      ofType(BankAccountsActions.updateBankAccountRequested),
      switchMap(({ id, payload }) =>
        this.bankAccountService.updateBankAccount(id, payload).pipe(
          map((bankAccount) => BankAccountsActions.updateBankAccountSucceeded({ bankAccount })),
          catchError(() =>
            of(
              BankAccountsActions.updateBankAccountFailed({
                errorMessage: 'errors.bankAccounts.unableToUpdate',
              })
            )
          )
        )
      )
    )
  );

  readonly deleteBankAccount$ = createEffect(() =>
    this.actions$.pipe(
      ofType(BankAccountsActions.deleteBankAccountRequested),
      switchMap(({ id }) =>
        this.bankAccountService.deleteBankAccount(id).pipe(
          map(() => BankAccountsActions.deleteBankAccountSucceeded({ id })),
          catchError(() =>
            of(
              BankAccountsActions.deleteBankAccountFailed({
                errorMessage: 'errors.bankAccounts.unableToDelete',
              })
            )
          )
        )
      )
    )
  );

  readonly reloadBankAccountsAfterShopSwitch$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopSucceeded, ShopsActions.setDefaultShopSucceeded),
      map(() => BankAccountsActions.loadBankAccountsRequested())
    )
  );
}
