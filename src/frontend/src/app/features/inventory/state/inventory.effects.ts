import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, map, of, switchMap, withLatestFrom } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { hasProblemCode } from '../../../shared/utils/problem-code.util';
import { ShopsActions } from '../../shops/state/shops.actions';
import { InventoryService } from '../services/inventory.service';
import { InventoryActions } from './inventory.actions';
import { selectInventoryLatestQuery } from './inventory.selectors';

@Injectable()
export class InventoryEffects {
  private readonly actions$ = inject(Actions);
  private readonly store = inject(Store);
  private readonly inventoryService = inject(InventoryService);

  readonly loadItems$ = createEffect(() =>
    this.actions$.pipe(
      ofType(InventoryActions.loadItemsRequested),
      withLatestFrom(this.store.select(selectInventoryLatestQuery)),
      switchMap(([{ query }, latestQuery]) =>
        this.inventoryService.getItems(query ?? latestQuery).pipe(
          map(({ items, totalCount, pageNumber, pageSize, summary }) =>
            InventoryActions.loadItemsSucceeded({
              items,
              totalCount,
              pageNumber,
              pageSize,
              summary,
            })
          ),
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
                errorMessage: getAddItemErrorMessage(error),
              })
            )
          )
        )
      )
    )
  );

  readonly reloadItemsAfterAdd$ = createEffect(() =>
    this.actions$.pipe(
      ofType(InventoryActions.addItemSucceeded),
      withLatestFrom(this.store.select(selectInventoryLatestQuery)),
      map(([, query]) => InventoryActions.loadItemsRequested({ query }))
    )
  );

  readonly updateItem$ = createEffect(() =>
    this.actions$.pipe(
      ofType(InventoryActions.updateItemRequested),
      switchMap(({ itemId, payload }) =>
        this.inventoryService.updateItem(itemId, payload).pipe(
          map(() => InventoryActions.updateItemSucceeded()),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              InventoryActions.updateItemFailed({
                errorMessage: getUpdateItemErrorMessage(error),
              })
            )
          )
        )
      )
    )
  );

  readonly reloadItemsAfterUpdate$ = createEffect(() =>
    this.actions$.pipe(
      ofType(InventoryActions.updateItemSucceeded),
      withLatestFrom(this.store.select(selectInventoryLatestQuery)),
      map(([, query]) => InventoryActions.loadItemsRequested({ query }))
    )
  );

  readonly reloadItemsAfterShopSwitch$ = createEffect(() =>
    this.actions$.pipe(
      ofType(ShopsActions.createShopSucceeded, ShopsActions.setDefaultShopSucceeded),
      withLatestFrom(this.store.select(selectInventoryLatestQuery)),
      map(([, query]) => InventoryActions.loadItemsRequested({ query }))
    )
  );
}

function getLoadItemsErrorMessage(_: ApiErrorPayload | undefined): string {
  return 'errors.items.unableToLoadItems';
}

function getAddItemErrorMessage(response: unknown): string {
  const title = (response as { error?: ApiErrorPayload } | null)?.error?.title ?? '';

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

  const unitError = getItemUnitErrorMessage(response);
  if (unitError) {
    return unitError;
  }

  return 'errors.items.unableToAddItem';
}

function getUpdateItemErrorMessage(response: unknown): string {
  const title = (response as { error?: ApiErrorPayload } | null)?.error?.title ?? '';

  if (title === 'Shop.ActiveShopNotSelected') {
    return 'errors.items.activeShopNotSelected';
  }

  if (title === 'Item.UserIsNotOwnerOrManager') {
    return 'errors.items.onlyOwnerOrManagerCanManageItems';
  }

  if (title === 'Item.ItemNotFound') {
    return 'errors.items.itemNotFound';
  }

  if (title === 'Item.BarcodeAlreadyExists') {
    return 'errors.items.barcodeAlreadyExists';
  }

  if (title === 'Item.NameRequired') {
    return 'errors.items.nameRequired';
  }

  if (title === 'Item.BarcodeRequired') {
    return 'errors.items.barcodeRequired';
  }

  const unitError = getItemUnitErrorMessage(response);
  if (unitError) {
    return unitError;
  }

  return 'errors.items.unableToUpdateItem';
}

/** Unit and precision refusals: validators answer under errors[].code, handlers under the title. */
function getItemUnitErrorMessage(response: unknown): string | null {
  if (hasProblemCode(response, 'Item.UomRequired')) {
    return 'errors.items.uomRequired';
  }

  if (hasProblemCode(response, 'Item.UnitChangeWithHistory')) {
    return 'errors.items.unitChangeWithHistory';
  }

  if (hasProblemCode(response, 'Item.QuantityScaleInvalid')) {
    return 'errors.items.quantityScaleInvalid';
  }

  if (hasProblemCode(response, 'Item.QuantityScaleMismatch')) {
    return 'errors.items.quantityScaleMismatch';
  }

  return null;
}
