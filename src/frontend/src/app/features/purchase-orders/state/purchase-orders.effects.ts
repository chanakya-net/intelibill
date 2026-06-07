import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { DEFAULT_PURCHASE_ORDER_LIST_FILTERS, PurchaseOrderService } from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';

@Injectable()
export class PurchaseOrdersEffects {
  private readonly actions$ = inject(Actions);
  private readonly service = inject(PurchaseOrderService);

  readonly loadOrders$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.loadPurchaseOrdersRequested),
      switchMap(({ filters }) =>
        this.service.getPurchaseOrders({ ...DEFAULT_PURCHASE_ORDER_LIST_FILTERS, ...filters }).pipe(
          map((result) => PurchaseOrdersActions.loadPurchaseOrdersSucceeded({ result })),
          catchError(() =>
            of(
              PurchaseOrdersActions.loadPurchaseOrdersFailed({
                errorMessage: 'purchaseOrders.errors.unableToLoad',
              })
            )
          )
        )
      )
    )
  );

  readonly loadDetail$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.loadPurchaseOrderDetailRequested),
      switchMap(({ purchaseOrderId }) =>
        this.service.getPurchaseOrderDetail(purchaseOrderId).pipe(
          map((order) => PurchaseOrdersActions.loadPurchaseOrderDetailSucceeded({ order })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              PurchaseOrdersActions.loadPurchaseOrderDetailFailed({
                errorMessage: getPurchaseOrderDetailErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly createDraft$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.createDraftRequested),
      switchMap(({ payload }) =>
        this.service.createDraft(payload).pipe(
          map((order) => PurchaseOrdersActions.createDraftSucceeded({ order })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              PurchaseOrdersActions.createDraftFailed({
                errorMessage: getCreateDraftErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );
}

function getPurchaseOrderDetailErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToLoadDetail';
}

function getCreateDraftErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotCreatePurchaseOrder') return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.InvalidLineQuantity') return 'purchaseOrders.errors.invalidLineQuantity';
  if (title === 'PurchaseOrder.InvalidLineUnitCost') return 'purchaseOrders.errors.invalidLineUnitCost';
  if (title === 'PurchaseOrder.LineDescriptionRequired') return 'purchaseOrders.errors.lineDescriptionRequired';
  if (title === 'PurchaseOrder.DuplicateItem') return 'purchaseOrders.errors.duplicateItem';
  return 'purchaseOrders.errors.unableToCreate';
}
