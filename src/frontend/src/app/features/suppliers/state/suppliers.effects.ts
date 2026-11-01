import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Observable, catchError, map, mergeMap, of, switchMap } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { ApiErrorPayload } from '../../../core/auth/auth.models';
import {
  FinancialOperationIntent,
  FinancialOperationIntentService,
  FinancialOperationScope,
  classifyFinancialOperationFailure,
} from '../../../core/services/financial-operation-intent.service';
import { ShopsActions } from '../../shops/state/shops.actions';
import { MakePaymentRequest, SupplierLedgerService } from '../services/supplier-ledger.service';
import { SupplierService } from '../services/supplier.service';
import { SuppliersActions } from './suppliers.actions';

@Injectable()
export class SuppliersEffects {
  private readonly actions$ = inject(Actions);
  private readonly supplierService = inject(SupplierService);
  private readonly ledgerService = inject(SupplierLedgerService);
  private readonly intents = inject(FinancialOperationIntentService);
  private readonly authService = inject(AuthService);

  readonly loadSuppliers$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SuppliersActions.loadSuppliersRequested),
      switchMap(() =>
        this.supplierService.getSuppliers().pipe(
          map((suppliers) => SuppliersActions.loadSuppliersSucceeded({ suppliers })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              SuppliersActions.loadSuppliersFailed({
                errorMessage: getLoadSuppliersErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly addSupplier$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SuppliersActions.addSupplierRequested),
      switchMap(({ payload }) =>
        this.supplierService.addSupplier(payload).pipe(
          map((supplier) => SuppliersActions.addSupplierSucceeded({ supplier })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              SuppliersActions.addSupplierFailed({
                errorMessage: getSupplierMutationErrorMessage(error.error, true),
              })
            )
          )
        )
      )
    )
  );

  readonly editSupplier$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SuppliersActions.editSupplierRequested),
      switchMap(({ supplierId, payload }) =>
        this.supplierService.editSupplier(supplierId, payload).pipe(
          map((supplier) => SuppliersActions.editSupplierSucceeded({ supplier })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              SuppliersActions.editSupplierFailed({
                errorMessage: getSupplierMutationErrorMessage(error.error, false),
              })
            )
          )
        )
      )
    )
  );

  readonly loadSupplierLedger$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SuppliersActions.loadSupplierLedgerRequested),
      switchMap(({ supplierId }) =>
        this.ledgerService.getSupplierLedgerEntries(supplierId).pipe(
          map((entries) => SuppliersActions.loadSupplierLedgerSucceeded({ supplierId, entries })),
          catchError(() =>
            of(SuppliersActions.loadSupplierLedgerFailed({ errorMessage: 'errors.suppliers.unableToLoadLedger' }))
          )
        )
      )
    )
  );

  /**
   * A new payment is frozen as an intent before it is sent. mergeMap, not switchMap: a later
   * payment must never cancel one already in flight.
   */
  readonly makePayment$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SuppliersActions.makePaymentRequested),
      mergeMap(({ supplierId, payload }) => {
        const scope = this.scope();
        if (this.intents.pending(scope, 'supplier-payment', supplierId)) {
          return of(SuppliersActions.makePaymentFailed({ errorMessage: 'financialOperations.pendingBlocksNew' }));
        }
        let intent: FinancialOperationIntent<MakePaymentRequest>;
        try {
          intent = this.intents.prepare(scope, 'supplier-payment', supplierId, payload);
        } catch {
          return of(SuppliersActions.makePaymentFailed({ errorMessage: 'financialOperations.persistFailed' }));
        }
        return this.sendPayment(scope, supplierId, intent);
      })
    )
  );

  readonly retryPayment$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SuppliersActions.retryPaymentRequested),
      mergeMap(({ supplierId }) => {
        const scope = this.scope();
        const intent = this.intents.pending<MakePaymentRequest>(scope, 'supplier-payment', supplierId);
        return intent
          ? this.sendPayment(scope, supplierId, intent)
          : of(SuppliersActions.makePaymentFailed({ errorMessage: '' }));
      })
    )
  );

  readonly reloadSuppliersAfterShopSwitch$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopSucceeded, ShopsActions.setDefaultShopSucceeded),
      map(() => SuppliersActions.loadSuppliersRequested())
    )
  );

  private scope(): FinancialOperationScope {
    const session = this.authService.session();
    return { userId: session?.user?.id ?? '', shopId: session?.activeShopId ?? '' };
  }

  private sendPayment(
    scope: FinancialOperationScope,
    supplierId: string,
    intent: FinancialOperationIntent<MakePaymentRequest>
  ): Observable<ReturnType<typeof SuppliersActions.makePaymentSucceeded | typeof SuppliersActions.makePaymentFailed>> {
    return this.intents
      .send(scope, intent, (payload, operationKey) =>
        this.ledgerService.makePayment(supplierId, { ...payload, operationKey })
      )
      .pipe(
        map((entry) => SuppliersActions.makePaymentSucceeded({ entry })),
        catchError((error: { error?: ApiErrorPayload }) =>
          of(
            SuppliersActions.makePaymentFailed({
              errorMessage:
                classifyFinancialOperationFailure(error) === 'uncertain'
                  ? 'financialOperations.outcomeUnknown'
                  : getPaymentErrorMessage(error.error),
            })
          )
        )
      );
  }
}

function getLoadSuppliersErrorMessage(_: ApiErrorPayload | undefined): string {
  return 'errors.suppliers.unableToLoadSuppliers';
}

function getPaymentErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'Supplier.UserIsNotOwnerOrManager') return 'errors.suppliers.onlyOwnerOrManagerCanMakePayment';
  if (title === 'Supplier.SupplierNotFound') return 'errors.suppliers.supplierNotFound';
  if (title === 'Supplier.PaymentAmountMustBePositive') return 'errors.suppliers.paymentAmountMustBePositive';
  return 'errors.suppliers.unableToMakePayment';
}

function getSupplierMutationErrorMessage(error: ApiErrorPayload | undefined, isAdd: boolean): string {
  const title = error?.title ?? '';

  if (title === 'Shop.UserIsNotOwner' || title === 'Supplier.UserIsNotOwner') {
    return 'errors.suppliers.onlyOwnerCanManageSuppliers';
  }

  if (title === 'Supplier.SupplierNotFound') {
    return 'errors.suppliers.supplierNotFound';
  }

  if (title === 'Supplier.ContactPersonPhoneInvalid') {
    return 'errors.suppliers.invalidContactPhone';
  }

  return isAdd ? 'errors.suppliers.unableToAddSupplier' : 'errors.suppliers.unableToUpdateSupplier';
}