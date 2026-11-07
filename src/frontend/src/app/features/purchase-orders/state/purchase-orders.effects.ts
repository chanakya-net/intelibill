import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Router } from '@angular/router';
import { catchError, map, of, switchMap, tap, mergeMap, from, EMPTY, firstValueFrom } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { ScopeMismatchError } from '../../../core/context/scoped-request';
import { ReceivingDraftService } from '../services/receiving-draft.service';
import { ReceivingCommand, ReceivingCorrelation, receivingKey } from '../services/receiving.models';
import {
  classifyReceivingFailure,
  normalizeReceivingErrors,
  receivingMessage,
  receivingProblem,
} from '../services/receiving-errors';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import {
  DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  PurchaseOrderService,
} from '../services/purchase-order.service';
import { PurchaseOrdersActions } from './purchase-orders.actions';

@Injectable()
export class PurchaseOrdersEffects {
  private readonly actions$ = inject(Actions);
  private readonly service = inject(PurchaseOrderService);
  private readonly router = inject(Router);
  private readonly drafts = inject(ReceivingDraftService);
  private readonly context = inject(ShopContextService);
  private readonly receivingInFlight = new Set<string>();

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
              }),
            ),
          ),
        ),
      ),
    ),
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
              }),
            ),
          ),
        ),
      ),
    ),
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
              }),
            ),
          ),
        ),
      ),
    ),
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
              }),
            ),
          ),
        ),
      ),
    ),
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
              }),
            ),
          ),
        ),
      ),
    ),
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
              }),
            ),
          ),
        ),
      ),
    ),
  );

  readonly deleteDraftSucceeded$ = createEffect(
    () =>
      this.actions$.pipe(
        ofType(PurchaseOrdersActions.deleteDraftSucceeded),
        tap(() => {
          void this.router.navigate(['/inventory/purchase-orders']);
        }),
      ),
    { dispatch: false },
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
              }),
            ),
          ),
        ),
      ),
    ),
  );

  readonly receiveOrder$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.receiveOrderRequested),
      mergeMap((command) =>
        this.runReceiving(command.correlation, () => this.sendReceipt(command)),
      ),
    ),
  );

  readonly checkReceipt$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.checkReceiptRequested),
      mergeMap(({ correlation }) =>
        this.runReceiving(correlation, () => this.lookupReceipt(correlation)),
      ),
    ),
  );

  // A replay response is an issue-time snapshot. Refresh current views after durable confirmation.
  readonly receiptConfirmed$ = createEffect(() =>
    this.actions$.pipe(
      ofType(
        PurchaseOrdersActions.receiveOrderSucceeded,
        PurchaseOrdersActions.checkReceiptSucceeded,
      ),
      mergeMap(({ correlation }) =>
        this.isCurrent(correlation)
          ? [
              PurchaseOrdersActions.loadPurchaseOrderDetailRequested({
                purchaseOrderId: correlation.purchaseOrderId,
              }),
              PurchaseOrdersActions.loadPurchaseOrdersRequested({ filters: {} }),
            ]
          : [],
      ),
    ),
  );

  private runReceiving<T>(correlation: ReceivingCorrelation, work: () => Promise<T>) {
    const key = receivingKey(correlation);
    if (this.receivingInFlight.has(key)) return EMPTY;
    this.receivingInFlight.add(key);
    const mutation = this.context.beginMutation();
    return from(
      work().finally(() => {
        this.receivingInFlight.delete(key);
        mutation.release();
      }),
    ).pipe(mergeMap((result) => (this.isCurrent(correlation) ? of(result) : EMPTY)));
  }

  private isCurrent(correlation: ReceivingCorrelation): boolean {
    return (
      this.context.isCurrent(correlation.scope) && this.context.epoch() === correlation.scope.epoch
    );
  }

  private async sendReceipt(command: ReceivingCommand) {
    const { correlation, payload } = command;
    let wasUnknown = command.recovery;
    try {
      const stored = await this.drafts.load(correlation.scope, correlation.purchaseOrderId);
      wasUnknown ||= stored?.pending?.status === 'unknown';
      if (
        !stored?.pending ||
        stored.pending.operationId !== correlation.operationId ||
        JSON.stringify(stored.pending.payload) !== JSON.stringify(payload)
      )
        throw new ScopeMismatchError();
      if (!this.isCurrent(correlation)) throw new ScopeMismatchError();
      const order = await firstValueFrom(
        this.service.receivePurchaseOrder(
          correlation.purchaseOrderId,
          stored.pending.payload,
          correlation.scope,
        ),
      );
      if (!this.isCurrent(correlation)) throw new ScopeMismatchError();
      if (order.purchaseOrderId !== correlation.purchaseOrderId)
        throw new Error('Receipt target mismatch.');
      await this.drafts.settle(
        correlation.scope,
        correlation.purchaseOrderId,
        correlation.operationId,
        'confirmed',
      );
      return PurchaseOrdersActions.receiveOrderSucceeded({ correlation });
    } catch (error) {
      let outcome = classifyReceivingFailure(error, wasUnknown);
      const problemCode = receivingProblem(error).code ?? receivingProblem(error).title ?? '';
      let errorMessage =
        problemCode === 'Operation.PayloadMismatch'
          ? receivingMessage(problemCode)
          : outcome === 'unknown'
            ? 'purchaseOrders.receiving.unknown'
            : receivingMessage(receivingProblem(error).code ?? receivingProblem(error).title ?? '');
      try {
        if (outcome === 'unknown')
          await this.drafts.markUnknown(
            correlation.scope,
            correlation.purchaseOrderId,
            correlation.operationId,
          );
        else
          await this.drafts.settle(
            correlation.scope,
            correlation.purchaseOrderId,
            correlation.operationId,
            'rejected',
          );
      } catch {
        outcome = 'unknown';
        errorMessage = 'purchaseOrders.receiving.storageError';
      }
      return PurchaseOrdersActions.receiveOrderFailed({
        correlation,
        outcome,
        errorMessage,
        rowErrors: normalizeReceivingErrors(receivingProblem(error), payload),
      });
    }
  }

  private async lookupReceipt(correlation: ReceivingCorrelation) {
    try {
      const stored = await this.drafts.load(correlation.scope, correlation.purchaseOrderId);
      if (stored?.pending?.operationId !== correlation.operationId || !this.isCurrent(correlation))
        throw new ScopeMismatchError();
      const lookup = await firstValueFrom(
        this.service.getReceiptOperation(correlation.operationId, correlation.scope),
      );
      if (!this.isCurrent(correlation)) throw new ScopeMismatchError();
      if (
        lookup.operationKey !== correlation.operationId ||
        lookup.targetId !== correlation.purchaseOrderId ||
        lookup.response.purchaseOrderId !== correlation.purchaseOrderId ||
        lookup.operationType !== 'purchase-order-receipt'
      )
        throw new Error('Receipt lookup identity mismatch.');
      await this.drafts.settle(
        correlation.scope,
        correlation.purchaseOrderId,
        correlation.operationId,
        'confirmed',
      );
      return PurchaseOrdersActions.checkReceiptSucceeded({ correlation });
    } catch (error) {
      try {
        await this.drafts.markUnknown(
          correlation.scope,
          correlation.purchaseOrderId,
          correlation.operationId,
        );
      } catch {
        /* Prepared remains frozen and recoverable. */
      }
      if (error instanceof HttpErrorResponse && error.status === 404)
        return PurchaseOrdersActions.checkReceiptMissing({ correlation });
      return PurchaseOrdersActions.checkReceiptFailed({
        correlation,
        errorMessage: 'purchaseOrders.receiving.lookupFailed',
      });
    }
  }

  readonly closeOrder$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.closeOrderRequested),
      switchMap(({ purchaseOrderId, payload }) =>
        this.service.closePurchaseOrder(purchaseOrderId, payload).pipe(
          map((order) => PurchaseOrdersActions.closeOrderSucceeded({ order })),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              PurchaseOrdersActions.closeOrderFailed({
                errorMessage: getCloseOrderErrorMessage(error.error),
              }),
            ),
          ),
        ),
      ),
    ),
  );
}

function getPurchaseOrderDetailErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToLoadDetail';
}

function getCreateDraftErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotCreatePurchaseOrder')
    return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.InvalidLineQuantity')
    return 'purchaseOrders.errors.invalidLineQuantity';
  if (title === 'PurchaseOrder.InvalidLineUnitCost')
    return 'purchaseOrders.errors.invalidLineUnitCost';
  if (title === 'PurchaseOrder.LineDescriptionRequired')
    return 'purchaseOrders.errors.lineDescriptionRequired';
  if (title === 'PurchaseOrder.DuplicateItem') return 'purchaseOrders.errors.duplicateItem';
  return 'purchaseOrders.errors.unableToCreate';
}

function getUpdateDraftErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotCreatePurchaseOrder')
    return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.InvalidLineQuantity')
    return 'purchaseOrders.errors.invalidLineQuantity';
  if (title === 'PurchaseOrder.InvalidLineUnitCost')
    return 'purchaseOrders.errors.invalidLineUnitCost';
  if (title === 'PurchaseOrder.LineDescriptionRequired')
    return 'purchaseOrders.errors.lineDescriptionRequired';
  if (title === 'PurchaseOrder.DuplicateItem') return 'purchaseOrders.errors.duplicateItem';
  if (title === 'PurchaseOrder.CannotUpdateNonDraft')
    return 'purchaseOrders.errors.cannotUpdateNonDraft';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToUpdate';
}

function getPlaceOrderErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotMutatePurchaseOrder')
    return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.SupplierRequired') return 'purchaseOrders.errors.supplierRequired';
  if (title === 'PurchaseOrder.SupplierInvalidForPlacement')
    return 'purchaseOrders.errors.supplierInvalidForPlacement';
  if (title === 'PurchaseOrder.AtLeastOneLineRequired')
    return 'purchaseOrders.errors.atLeastOneLineRequired';
  if (title === 'PurchaseOrder.CannotPlaceNonDraft')
    return 'purchaseOrders.errors.cannotPlaceNonDraft';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToPlace';
}

function getDeleteDraftErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotMutatePurchaseOrder')
    return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.CannotDeleteNonDraft')
    return 'purchaseOrders.errors.cannotDeleteNonDraft';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToDelete';
}

function getCancelOrderErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotMutatePurchaseOrder')
    return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.CannotCancelAfterReceipt')
    return 'purchaseOrders.errors.cannotCancelAfterReceipt';
  if (title === 'PurchaseOrder.CannotCancelInvalidStatus')
    return 'purchaseOrders.errors.cannotCancelInvalidStatus';
  if (title === 'PurchaseOrder.CancellationReasonRequired')
    return 'purchaseOrders.errors.cancellationReasonRequired';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToCancel';
}

function getCloseOrderErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';
  if (title === 'PurchaseOrder.UserCannotMutatePurchaseOrder')
    return 'purchaseOrders.errors.forbidden';
  if (title === 'PurchaseOrder.CannotCloseInvalidStatus')
    return 'purchaseOrders.errors.cannotCloseInvalidStatus';
  if (title === 'PurchaseOrder.CloseReasonRequired')
    return 'purchaseOrders.errors.closeReasonRequired';
  if (title === 'PurchaseOrder.NotFound') return 'purchaseOrders.errors.notFound';
  return 'purchaseOrders.errors.unableToClose';
}
