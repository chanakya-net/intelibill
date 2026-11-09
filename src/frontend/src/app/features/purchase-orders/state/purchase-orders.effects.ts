import {
  ApiErrorMessageService,
  type LocalizedError,
} from '../../../core/i18n/api-error-message.service';
import { Store } from '@ngrx/store';
import { selectPurchaseOrdersState } from './purchase-orders.selectors';
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
  private readonly apiErrors = inject(ApiErrorMessageService);
  private readonly actions$ = inject(Actions);
  private readonly service = inject(PurchaseOrderService);
  private readonly router = inject(Router);
  private readonly drafts = inject(ReceivingDraftService);
  private readonly context = inject(ShopContextService);
  private readonly state = inject(Store).selectSignal(selectPurchaseOrdersState);
  private readonly receivingInFlight = new Set<string>();

  readonly loadOrders$ = createEffect(() =>
    this.actions$.pipe(
      ofType(PurchaseOrdersActions.loadPurchaseOrdersRequested),
      switchMap(({ filters }) =>
        this.service.getPurchaseOrders({ ...DEFAULT_PURCHASE_ORDER_LIST_FILTERS, ...filters }).pipe(
          map((result) => PurchaseOrdersActions.loadPurchaseOrdersSucceeded({ result })),
          catchError((error) =>
            of(
              PurchaseOrdersActions.loadPurchaseOrdersFailed({
                ...this.feedback(error, 'purchaseOrders.errors.unableToLoad'),
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
                ...this.feedback(error, 'purchaseOrders.errors.unableToLoadDetail'),
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
                ...this.feedback(error, 'purchaseOrders.errors.unableToCreate'),
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
                ...this.feedback(error, 'purchaseOrders.errors.unableToUpdate'),
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
                ...this.feedback(error, 'purchaseOrders.errors.unableToPlace'),
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
                ...this.feedback(error, 'purchaseOrders.errors.unableToDelete'),
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
                ...this.feedback(error, 'purchaseOrders.errors.unableToCancel'),
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
      mergeMap(({ correlation }) => {
        if (!this.isCurrent(correlation)) return [];
        const state = this.state();
        const activeTarget = state.detailPurchaseOrderId ?? state.selectedOrder?.purchaseOrderId;
        return [
          ...(activeTarget === correlation.purchaseOrderId
            ? [
                PurchaseOrdersActions.loadPurchaseOrderDetailRequested({
                  purchaseOrderId: correlation.purchaseOrderId,
                }),
              ]
            : []),
          PurchaseOrdersActions.loadPurchaseOrdersRequested({ filters: state.filters }),
        ];
      }),
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
    try {
      if (!this.isCurrent(correlation)) throw new ScopeMismatchError();
      const stored = command.recovery
        ? await this.drafts.claimRecovery(
            correlation.scope,
            correlation.purchaseOrderId,
            correlation.operationId,
            payload,
          )
        : await this.drafts.load(correlation.scope, correlation.purchaseOrderId);
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
      if (!(await this.confirmReceipt(correlation)))
        throw new Error('Receipt confirmation did not match durable work.');
      return PurchaseOrdersActions.receiveOrderSucceeded({ correlation });
    } catch (error) {
      let outcome: 'unknown' | 'rejected' = 'unknown';
      let storageFailed = false;
      try {
        const settled = await this.drafts.settleFailure(
          correlation.scope,
          correlation.purchaseOrderId,
          correlation.operationId,
          error,
          command.recovery,
        );
        if (
          settled.record?.confirmedOperationId === correlation.operationId ||
          (!settled.record &&
            (await this.drafts.isConfirmed(
              correlation.scope,
              correlation.purchaseOrderId,
              correlation.operationId,
            )))
        )
          return PurchaseOrdersActions.receiveOrderSucceeded({ correlation });
        outcome = settled.outcome;
      } catch {
        storageFailed = true;
      }
      const problemCode = receivingProblem(error).code ?? receivingProblem(error).title ?? '';
      const errorMessage = storageFailed
        ? 'purchaseOrders.receiving.storageError'
        : problemCode === 'Operation.PayloadMismatch'
          ? receivingMessage(problemCode)
          : outcome === 'unknown'
            ? 'purchaseOrders.receiving.unknown'
            : receivingMessage(problemCode);
      return PurchaseOrdersActions.receiveOrderFailed({
        correlation,
        outcome,
        ...this.feedback(error, errorMessage, true),
        errorMessage,
        rowErrors: normalizeReceivingErrors(receivingProblem(error), payload),
      });
    }
  }

  private async confirmReceipt(correlation: ReceivingCorrelation): Promise<boolean> {
    const record = await this.drafts.settle(
      correlation.scope,
      correlation.purchaseOrderId,
      correlation.operationId,
      'confirmed',
    );
    // An unmatched settlement is not proof. Only an exact tombstone, or its immutable archived
    // proof after cleanup, may acknowledge the operation; a replacement record is untouched.
    return (
      record?.confirmedOperationId === correlation.operationId ||
      (!record &&
        (await this.drafts.isConfirmed(
          correlation.scope,
          correlation.purchaseOrderId,
          correlation.operationId,
        )))
    );
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
      if (!(await this.confirmReceipt(correlation)))
        throw new Error('Receipt confirmation did not match durable work.');
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
        ...this.feedback(error, 'purchaseOrders.receiving.lookupFailed', true),
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
                ...this.feedback(error, 'purchaseOrders.errors.unableToClose'),
              }),
            ),
          ),
        ),
      ),
    ),
  );
  private feedback(
    error: unknown,
    fallbackKey: string,
    preserveKey = false,
  ): { errorMessage: string; localizedError?: LocalizedError } {
    const resolved = this.apiErrors.resolve(error, {
      fallbackKey,
      codeMap: PURCHASE_ORDER_ERROR_KEYS,
      fieldMap: {
        SupplierId: 'supplierId',
        SupplierReference: 'supplierReference',
        ExpectedDeliveryDate: 'expectedDeliveryDate',
      },
    });
    const localizedError = preserveKey ? { ...resolved, messageKey: fallbackKey } : resolved;
    return {
      errorMessage: localizedError.messageKey,
      ...(localizedError.correlationId || localizedError.fieldErrors.length
        ? { localizedError }
        : {}),
    };
  }
}

const PURCHASE_ORDER_ERROR_KEYS: Readonly<Record<string, string>> = {
  'PurchaseOrder.NotFound': 'purchaseOrders.errors.notFound',
  'PurchaseOrder.UserCannotCreatePurchaseOrder': 'purchaseOrders.errors.forbidden',
  'PurchaseOrder.InvalidLineQuantity': 'purchaseOrders.errors.invalidLineQuantity',
  'PurchaseOrder.InvalidLineUnitCost': 'purchaseOrders.errors.invalidLineUnitCost',
  'PurchaseOrder.LineDescriptionRequired': 'purchaseOrders.errors.lineDescriptionRequired',
  'PurchaseOrder.DuplicateItem': 'purchaseOrders.errors.duplicateItem',
  'PurchaseOrder.CannotUpdateNonDraft': 'purchaseOrders.errors.cannotUpdateNonDraft',
  'PurchaseOrder.UserCannotMutatePurchaseOrder': 'purchaseOrders.errors.forbidden',
  'PurchaseOrder.SupplierRequired': 'purchaseOrders.errors.supplierRequired',
  'PurchaseOrder.SupplierInvalidForPlacement': 'purchaseOrders.errors.supplierInvalidForPlacement',
  'PurchaseOrder.AtLeastOneLineRequired': 'purchaseOrders.errors.atLeastOneLineRequired',
  'PurchaseOrder.CannotPlaceNonDraft': 'purchaseOrders.errors.cannotPlaceNonDraft',
  'PurchaseOrder.CannotDeleteNonDraft': 'purchaseOrders.errors.cannotDeleteNonDraft',
  'PurchaseOrder.CannotCancelAfterReceipt': 'purchaseOrders.errors.cannotCancelAfterReceipt',
  'PurchaseOrder.CannotCancelInvalidStatus': 'purchaseOrders.errors.cannotCancelInvalidStatus',
  'PurchaseOrder.CancellationReasonRequired': 'purchaseOrders.errors.cancellationReasonRequired',
  'PurchaseOrder.CannotCloseInvalidStatus': 'purchaseOrders.errors.cannotCloseInvalidStatus',
  'PurchaseOrder.CloseReasonRequired': 'purchaseOrders.errors.closeReasonRequired',
};
