import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { ShopsActions } from '../../shops/state/shops.actions';
import { InventoryService } from '../services/inventory.service';
import { InventoryActions } from './inventory.actions';

@Injectable()
export class InventoryEffects {
  private readonly actions$ = inject(Actions);
  private readonly inventoryService = inject(InventoryService);

  readonly loadItems$ = createEffect(() =>
    this.actions$.pipe(
      ofType(InventoryActions.loadItemsRequested),
      switchMap(() =>
        this.inventoryService.getItems().pipe(
          map((items) => InventoryActions.loadItemsSucceeded({ items })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              InventoryActions.loadItemsFailed({
                errorMessage: getLoadItemsErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly addItem$ = createEffect(() =>
    this.actions$.pipe(
      ofType(InventoryActions.addItemRequested),
      switchMap(({ payload }) =>
        this.inventoryService.addItem(payload).pipe(
          map((item) => InventoryActions.addItemSucceeded({ item })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              InventoryActions.addItemFailed({
                errorMessage: getAddItemErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly reloadItemsAfterShopSwitch$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopSucceeded, ShopsActions.setDefaultShopSucceeded),
      map(() => InventoryActions.loadItemsRequested())
    )
  );
}

function getLoadItemsErrorMessage(_: ApiErrorPayload | undefined): string {
  return 'errors.items.unableToLoadItems';
}

function getAddItemErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Shop.ActiveShopNotSelected') {
    return 'errors.items.activeShopNotSelected';
  }

  if (title === 'Shop.UserIsNotOwner' || title === 'Item.UserIsNotOwner') {
    return 'errors.items.onlyOwnerOrManagerCanManageItems';
  }

  if (title === 'Item.NameRequired') {
    return 'errors.items.nameRequired';
  }

  if (title === 'Item.BarcodeRequired') {
    return 'errors.items.barcodeRequired';
  }

  if (title === 'Item.UomRequired') {
    return 'errors.items.uomRequired';
  }

  if (title === 'Item.PreferredSupplierNotFound') {
    return 'errors.items.preferredSupplierNotFound';
  }

  return 'errors.items.unableToAddItem';
}
