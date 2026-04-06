import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { ShopsActions } from '../../shops/state/shops.actions';
import { SupplierLedgerService } from '../services/supplier-ledger.service';
import { SupplierService } from '../services/supplier.service';
import { SuppliersActions } from './suppliers.actions';

@Injectable()
export class SuppliersEffects {
  private readonly actions$ = inject(Actions);
  private readonly supplierService = inject(SupplierService);
  private readonly ledgerService = inject(SupplierLedgerService);

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

  readonly reloadSuppliersAfterShopSwitch$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopSucceeded, ShopsActions.setDefaultShopSucceeded),
      map(() => SuppliersActions.loadSuppliersRequested())
    )
  );
}

function getLoadSuppliersErrorMessage(_: ApiErrorPayload | undefined): string {
  return 'errors.suppliers.unableToLoadSuppliers';
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