import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { EMPTY, catchError, map, of, switchMap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { ShopService } from '../services/shop.service';
import { ShopsActions } from './shops.actions';

@Injectable()
export class ShopsEffects {
  private readonly actions$ = inject(Actions);
  private readonly shopService = inject(ShopService);

  readonly loadShops$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.loadShopsRequested),
      switchMap(() =>
        this.shopService.getMyShops().pipe(
          map((shops) => ShopsActions.loadShopsSucceeded({ shops })),
          catchError(() =>
            of(
              ShopsActions.loadShopsFailed({
                errorMessage: 'errors.shops.unableToLoadShops',
              })
            )
          )
        )
      )
    )
  );

  readonly loadShopDetails$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.loadShopDetailsRequested),
      switchMap(({ shopId }) =>
        this.shopService.getShopDetails(shopId).pipe(
          map((details) => ShopsActions.loadShopDetailsSucceeded({ details })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              ShopsActions.loadShopDetailsFailed({
                errorMessage: getShopDetailsErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly loadActiveShopDetailsAfterShopsLoad$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.loadShopsSucceeded),
      switchMap(({ shops }) => {
        const activeShop = shops.find((shop) => shop.isDefault) ?? shops[0];
        if (!activeShop) {
          return EMPTY;
        }

        return of(ShopsActions.loadShopDetailsRequested({ shopId: activeShop.shopId }));
      })
    )
  );

  readonly createShop$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopRequested),
      switchMap(({ payload }) =>
        this.shopService.createShop(payload).pipe(
          map(() => ShopsActions.createShopSucceeded()),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              ShopsActions.createShopFailed({
                errorMessage: getShopMutationErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly updateShop$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.updateShopRequested),
      switchMap(({ shopId, payload }) =>
        this.shopService.updateShop(shopId, payload).pipe(
          map((details) => ShopsActions.updateShopSucceeded({ details })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              ShopsActions.updateShopFailed({
                errorMessage: getShopMutationErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly setDefaultShop$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.setDefaultShopRequested),
      switchMap(({ shopId }) =>
        this.shopService.setDefaultShop(shopId).pipe(
          map(() => ShopsActions.setDefaultShopSucceeded()),
          catchError(() =>
            of(
              ShopsActions.setDefaultShopFailed({
                errorMessage: 'errors.shops.unableToSetDefaultStore',
              })
            )
          )
        )
      )
    )
  );

  readonly updateShopBankDetails$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.updateShopBankDetailsRequested),
      switchMap(({ shopId, payload }) =>
        this.shopService.updateBankDetails(shopId, payload).pipe(
          map((details) => ShopsActions.updateShopBankDetailsSucceeded({ details })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              ShopsActions.updateShopBankDetailsFailed({
                errorMessage: getBankDetailsErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly refreshShopsAfterMutation$ = createEffect(() =>
    this.actions$.pipe(
      ofType(
        ShopsActions.createShopSucceeded,
        ShopsActions.setDefaultShopSucceeded,
        ShopsActions.updateShopSucceeded,
        ShopsActions.updateShopBankDetailsSucceeded
      ),
      map(() => ShopsActions.loadShopsRequested())
    )
  );
}

function getShopDetailsErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Shop.MembershipNotFound') {
    return 'errors.shops.membershipNotFound';
  }

  if (title === 'Shop.ShopNotFound') {
    return 'errors.shops.shopNotFound';
  }

  return 'errors.shops.unableToLoadDetails';
}

function getBankDetailsErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Unauthorized' || title === 'Auth.Unauthorized') {
    return 'errors.auth.sessionVerificationFailed';
  }

  if (title === 'Shop.UserIsNotOwner') {
    return 'errors.shops.onlyOwnersCanUpdate';
  }

  if (title === 'Shop.IfscCodeInvalid') {
    return 'errors.shops.ifscCodeInvalid';
  }

  if (title === 'Shop.BankAccountTypeInvalid') {
    return 'errors.shops.bankAccountTypeInvalid';
  }

  return 'errors.shops.unableToUpdateBankDetails';
}

function getShopMutationErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Unauthorized' || title === 'Auth.Unauthorized') {
    return 'errors.auth.sessionVerificationFailed';
  }

  if (title === 'Shop.UserIsNotOwner') {
    return 'errors.shops.onlyOwnersCanUpdate';
  }

  if (title === 'Shop.NameRequired') {
    return 'errors.shops.nameRequired';
  }

  if (title === 'Shop.AddressRequired') {
    return 'errors.shops.addressRequired';
  }

  if (title === 'Shop.CityRequired') {
    return 'errors.shops.cityRequired';
  }

  if (title === 'Shop.StateRequired') {
    return 'errors.shops.stateRequired';
  }

  if (title === 'Shop.PincodeRequired') {
    return 'errors.shops.pincodeRequired';
  }

  return 'errors.shops.unableToUpdateShop';
}
