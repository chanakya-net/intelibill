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
                errorMessage: 'Unable to load your shops right now. Please try again.',
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
                errorMessage: 'Unable to set default store right now. Please try again.',
              })
            )
          )
        )
      )
    )
  );

  readonly refreshShopsAfterMutation$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopSucceeded, ShopsActions.setDefaultShopSucceeded, ShopsActions.updateShopSucceeded),
      map(() => ShopsActions.loadShopsRequested())
    )
  );
}

function getShopDetailsErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Shop.MembershipNotFound') {
    return 'You do not have access to this shop.';
  }

  if (title === 'Shop.ShopNotFound') {
    return 'The selected shop no longer exists.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to load shop details right now. Please try again.';
}

function getShopMutationErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Unauthorized' || title === 'Auth.Unauthorized') {
    return 'Your session could not be verified for shop operation. Please sign in again.';
  }

  if (title === 'Shop.UserIsNotOwner') {
    return 'Only shop owners can update shop details.';
  }

  if (title === 'Shop.NameRequired') {
    return 'Shop name is required.';
  }

  if (title === 'Shop.AddressRequired') {
    return 'Shop address is required.';
  }

  if (title === 'Shop.CityRequired') {
    return 'Shop city is required.';
  }

  if (title === 'Shop.StateRequired') {
    return 'Shop state is required.';
  }

  if (title === 'Shop.PincodeRequired') {
    return 'Shop pincode is required.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to update shop right now. Please try again.';
}
