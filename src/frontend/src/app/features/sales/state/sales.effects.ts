import { saleQuantityErrorKey, saleReturnQuantityErrorKey } from '../utils/sale-quantity-error';
import {
  ApiErrorMessageService,
  type LocalizedError,
} from '../../../core/i18n/api-error-message.service';
import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import {
  EMPTY,
  catchError,
  firstValueFrom,
  from,
  map,
  mergeMap,
  of,
  switchMap,
  takeUntil,
} from 'rxjs';

import { isScopeChangeError } from '../../../core/context/scoped-request';
import { ShopContextService } from '../../../core/context/shop-context.service';
import {
  CheckoutIntentService,
  classifyCheckoutSendFailure,
} from '../services/checkout-intent.service';
import { SaleService } from '../services/sale.service';
import type { CheckoutIntent } from '../../../core/storage/checkout-intent-indexeddb.service';
import { INVOICE_ERROR_KEYS } from '../utils/invoice-error-keys';
import { CUSTOMER_CREDIT_ERROR_KEYS } from '../utils/customer-credit-error-keys';
import { SalesActions } from './sales.actions';

@Injectable()
export class SalesEffects {
  private readonly apiErrors = inject(ApiErrorMessageService);
  private readonly actions$ = inject(Actions);
  private readonly saleService = inject(SaleService);
  private readonly checkoutIntents = inject(CheckoutIntentService);
  private readonly shopContext = inject(ShopContextService);

  loadSales$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.loadSalesRequested),
      switchMap(({ queryParams }) =>
        this.saleService.getSales(queryParams).pipe(
          map(({ items, totalCount, pageNumber, pageSize, summary }) =>
            SalesActions.loadSalesSucceeded({
              sales: items,
              totalCount,
              pageNumber,
              pageSize,
              summary,
            }),
          ),
          catchError((error) =>
            of(
              SalesActions.loadSalesFailed({
                ...this.feedback(error, 'errors.sales.loadFailed'),
              }),
            ),
          ),
        ),
      ),
    ),
  );

  loadProfitLossReport$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.loadProfitLossReportRequested),
      switchMap(({ queryParams }) =>
        this.saleService.getProfitLossReport(queryParams).pipe(
          map((result) => SalesActions.loadProfitLossReportSucceeded({ result })),
          catchError((error) =>
            of(
              SalesActions.loadProfitLossReportFailed({
                ...this.feedback(error, 'errors.sales.profitLossLoadFailed'),
              }),
            ),
          ),
        ),
      ),
    ),
  );

  loadSaleDetail$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.loadSaleDetailRequested),
      switchMap(({ saleId }) =>
        this.saleService.getSaleById(saleId).pipe(
          map((sale) => SalesActions.loadSaleDetailSucceeded({ sale })),
          catchError((error) =>
            of(
              SalesActions.loadSaleDetailFailed({
                ...this.feedback(error, 'errors.sales.detailLoadFailed'),
              }),
            ),
          ),
        ),
      ),
    ),
  );

  previewSaleReturn$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.previewSaleReturnRequested),
      switchMap(({ saleId, payload }) =>
        this.saleService.previewSaleReturn(saleId, payload).pipe(
          map((preview) => SalesActions.previewSaleReturnSucceeded({ preview })),
          catchError((error) =>
            of(
              SalesActions.previewSaleReturnFailed({
                ...this.feedback(error, 'errors.sales.returnPreviewFailed'),
              }),
            ),
          ),
        ),
      ),
    ),
  );

  recordSaleReturn$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.recordSaleReturnRequested),
      switchMap(({ saleId, payload }) =>
        this.saleService.recordSaleReturn(saleId, payload).pipe(
          map((sale) => SalesActions.recordSaleReturnSucceeded({ sale })),
          catchError((error) =>
            isScopeChangeError(error)
              ? EMPTY
              : of(
                  SalesActions.recordSaleReturnFailed({
                    ...this.feedback(error, 'errors.sales.returnRecordFailed'),
                  }),
                ),
          ),
          // Durable settlement continues, while a left epoch can no longer update the store.
          takeUntil(this.shopContext.changes$),
        ),
      ),
    ),
  );

  voidSaleReturn$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.voidSaleReturnRequested),
      switchMap(({ saleId, saleReturnId, payload }) =>
        this.saleService.voidSaleReturn(saleReturnId, payload).pipe(
          switchMap(() => this.saleService.getSaleById(saleId)),
          map((sale) => SalesActions.voidSaleReturnSucceeded({ sale })),
          catchError((error) =>
            isScopeChangeError(error)
              ? EMPTY
              : of(
                  SalesActions.voidSaleReturnFailed({
                    ...this.feedback(error, 'errors.sales.returnVoidFailed'),
                  }),
                ),
          ),
          // Durable settlement continues, while a left epoch can no longer update the store.
          takeUntil(this.shopContext.changes$),
        ),
      ),
    ),
  );

  // mergeMap, not switchMap: a later checkout must never cancel an in-flight sale POST, whose
  // outcome would then be unknown.
  recordSale$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.recordSaleRequested),
      mergeMap(({ payload, intentId }) => {
        if (!intentId) {
          return this.saleService.recordSale(payload).pipe(
            map((sale) => SalesActions.recordSaleSucceeded({ sale })),
            catchError((error) =>
              of(
                SalesActions.recordSaleFailed({
                  ...this.feedback(error, 'errors.sales.recordFailed'),
                }),
              ),
            ),
          );
        }
        return from(this.sendIntent(intentId));
      }),
    ),
  );

  /**
   * A switch waits while a checkout is in flight; the wait ends once its outcome is stored on the
   * intent. An outcome that settles after the tab left the intent's scope stays on the intent and is
   * reported nowhere else.
   */
  private async sendIntent(intentId: string) {
    const mutation = this.shopContext.beginMutation();
    try {
      const intent = await this.checkoutIntents.claimForSend(intentId).catch(() => null);
      if (!intent) {
        // Another tab owns the send, or the intent already resolved: reconcile instead of sending.
        return SalesActions.recordSaleUncertain({
          intentId,
          errorMessage: 'sales.newSale.pendingCheckout.inProgressElsewhere',
        });
      }

      const outcome = await this.settleIntent(intent);
      return this.shopContext.isCurrent({ userId: intent.userId, shopId: intent.shopId })
        ? outcome
        : SalesActions.recordSaleDetached({ intentId });
    } finally {
      mutation.release();
    }
  }

  private async settleIntent(intent: CheckoutIntent) {
    const intentId = intent.intentId;
    try {
      const sale = await firstValueFrom(this.checkoutIntents.send(intent));
      await this.checkoutIntents.confirm(intentId, sale).catch(() => null);
      return SalesActions.recordSaleSucceeded({ sale, intentId });
    } catch (error) {
      if (classifyCheckoutSendFailure(error) === 'rejected') {
        const code = (error as { error?: { title?: string } }).error?.title ?? 'unknown';
        await this.checkoutIntents.reject(intentId, code).catch(() => null);
        return SalesActions.recordSaleFailed({
          intentId,
          ...this.feedback(error, 'errors.sales.recordFailed'),
        });
      }
      // Includes an answer lost to a scope change: it may have committed in the intent's own shop.
      await this.checkoutIntents.markUncertain(intentId).catch(() => null);
      return SalesActions.recordSaleUncertain({
        intentId,
        errorMessage: 'sales.newSale.pendingCheckout.uncertain',
      });
    }
  }
  private feedback(
    error: unknown,
    fallbackKey: string,
  ): { errorMessage: string; localizedError?: LocalizedError } {
    const quantityKey = saleReturnQuantityErrorKey(error) ?? saleQuantityErrorKey(error);
    const localizedError = this.apiErrors.resolve(error, {
      fallbackKey,
      codeMap: {
        ...INVOICE_ERROR_KEYS,
        ...CUSTOMER_CREDIT_ERROR_KEYS,
        ...(quantityKey ? { 'Validation.Failed': quantityKey } : {}),
        'Sale.QuantityPrecisionExceeded': 'sales.newSale.quantityPrecisionExceeded',
        'SaleReturn.QuantityPrecisionExceeded':
          'sales.returns.preview.validation.serverQuantityPrecision',
      },
      fieldMap: {
        PaidAmount: 'paidAmount',
        DueAmount: 'dueAmount',
        PlaceOfSupplyStateCode: 'placeOfSupplyStateCode',
      },
    });
    return {
      errorMessage: localizedError.messageKey,
      ...(localizedError.correlationId || localizedError.fieldErrors.length
        ? { localizedError }
        : {}),
    };
  }
}
