import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Router } from '@angular/router';
import { catchError, map, of, switchMap, tap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { DEFAULT_PURCHASE_ORDER_LIST_FILTERS, PurchaseOrderService } from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';

@Injectable()
export class PurchaseOrdersEffects {
  private readonly actions$ = inject(Actions);
  private readonly service = inject(PurchaseOrderService);
  private readonly router = inject(Router);

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

  readonly updateDraft$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.updateDraftRequested),
      switchMap(({ purchaseOrderId, payload }) =>
        this.service.updateDraft(purchaseOrderId, payload).pipe(
          map((order) => PurchaseOrdersActions.updateDraftSucceeded({ order })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              PurchaseOrdersActions.updateDraftFailed({
                errorMessage: getUpdateDraftErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly placeOrder$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.placeOrderRequested),
      switchMap(({ purchaseOrderId }) =>
        this.service.placePurchaseOrder(purchaseOrderId).pipe(
          map((order) => PurchaseOrdersActions.placeOrderSucceeded({ order })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              PurchaseOrdersActions.placeOrderFailed({
                errorMessage: getPlaceOrderErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly deleteDraft$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.deleteDraftRequested),
      switchMap(({ purchaseOrderId }) =>
        this.service.deleteDraft(purchaseOrderId).pipe(
          map(() => PurchaseOrdersActions.deleteDraftSucceeded({ purchaseOrderId })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              PurchaseOrdersActions.deleteDraftFailed({
                errorMessage: getDeleteDraftErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly deleteDraftSucceeded$ = createEffect(
    () =>
      this.actions$.pipe(
        ofType(PurchaseOrdersActions.deleteDraftSucceeded),
        tap(() => {
          void this.router.navigate(['/inventory/purchase-orders']);
        })
      ),
    { dispatch: false }
  );

  readonly cancelOrder$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.cancelOrderRequested),
      switchMap(({ purchaseOrderId, payload }) =>
        this.service.cancelPurchaseOrder(purchaseOrderId, payload).pipe(
          map((order) => PurchaseOrdersActions.cancelOrderSucceeded({ order })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              PurchaseOrdersActions.cancelOrderFailed({
                errorMessage: getCancelOrderErrorMessage(error.error),
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

function getUpdateDraftErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotCreatePurchaseOrder') return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.InvalidLineQuantity') return 'purchaseOrders.errors.invalidLineQuantity';
  if (title === 'PurchaseOrder.InvalidLineUnitCost') return 'purchaseOrders.errors.invalidLineUnitCost';
  if (title === 'PurchaseOrder.LineDescriptionRequired') return 'purchaseOrders.errors.lineDescriptionRequired';
  if (title === 'PurchaseOrder.DuplicateItem') return 'purchaseOrders.errors.duplicateItem';
  if (title === 'PurchaseOrder.CannotUpdateNonDraft') return 'purchaseOrders.errors.cannotUpdateNonDraft';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToUpdate';
}

function getPlaceOrderErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotMutatePurchaseOrder') return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.SupplierRequired') return 'purchaseOrders.errors.supplierRequired';
  if (title === 'PurchaseOrder.SupplierInvalidForPlacement') return 'purchaseOrders.errors.supplierInvalidForPlacement';
  if (title === 'PurchaseOrder.AtLeastOneLineRequired') return 'purchaseOrders.errors.atLeastOneLineRequired';
  if (title === 'PurchaseOrder.CannotPlaceNonDraft') return 'purchaseOrders.errors.cannotPlaceNonDraft';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToPlace';
}

function getDeleteDraftErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotMutatePurchaseOrder') return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.CannotDeleteNonDraft') return 'purchaseOrders.errors.cannotDeleteNonDraft';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToDelete';
}

function getCancelOrderErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotMutatePurchaseOrder') return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.CannotCancelAfterReceipt') return 'purchaseOrders.errors.cannotCancelAfterReceipt';
  if (title === 'PurchaseOrder.CannotCancelInvalidStatus') return 'purchaseOrders.errors.cannotCancelInvalidStatus';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToCancel';
}
