import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';

import { SaleService } from '../services/sale.service';
import { SalesActions } from './sales.actions';

@Injectable()
export class SalesEffects {
  private readonly actions$ = inject(Actions);
  private readonly saleService = inject(SaleService);

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
            })
          ),
          catchError((error) =>
            of(
              SalesActions.loadSalesFailed({
                errorMessage: error.error?.detail || 'Failed to load sales',
              })
            )
          )
        )
      )
    )
  );

  loadProfitLossReport$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.loadProfitLossReportRequested),
      switchMap(() =>
        this.saleService.getProfitLossReport().pipe(
          map((report) => SalesActions.loadProfitLossReportSucceeded({ report })),
          catchError((error) =>
            of(
              SalesActions.loadProfitLossReportFailed({
                errorMessage: error.error?.detail || 'Failed to load profit loss report',
              })
            )
          )
        )
      )
    )
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
                errorMessage: error.error?.detail || 'Failed to load sale details',
              })
            )
          )
        )
      )
    )
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
                errorMessage: error.error?.detail || error.error?.title || 'Failed to preview sale return',
              })
            )
          )
        )
      )
    )
  );

  recordSaleReturn$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.recordSaleReturnRequested),
      switchMap(({ saleId, payload }) =>
        this.saleService.recordSaleReturn(saleId, payload).pipe(
          map((sale) => SalesActions.recordSaleReturnSucceeded({ sale })),
          catchError((error) =>
            of(
              SalesActions.recordSaleReturnFailed({
                errorMessage: error.error?.detail || error.error?.title || 'Failed to record sale return',
              })
            )
          )
        )
      )
    )
  );

  voidSaleReturn$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.voidSaleReturnRequested),
      switchMap(({ saleId, saleReturnId, payload }) =>
        this.saleService.voidSaleReturn(saleReturnId, payload).pipe(
          switchMap(() => this.saleService.getSaleById(saleId)),
          map((sale) => SalesActions.voidSaleReturnSucceeded({ sale })),
          catchError((error) =>
            of(
              SalesActions.voidSaleReturnFailed({
                errorMessage: error.error?.detail || error.error?.title || 'Failed to void sale return',
              })
            )
          )
        )
      )
    )
  );

  recordSale$ = createEffect(() =>
    this.actions$.pipe(
      ofType(SalesActions.recordSaleRequested),
      switchMap(({ payload }) =>
        this.saleService.recordSale(payload).pipe(
          map((sale) => SalesActions.recordSaleSucceeded({ sale })),
          catchError((error) =>
            of(
              SalesActions.recordSaleFailed({
                errorMessage: error.error?.detail || 'Failed to record sale',
              })
            )
          )
        )
      )
    )
  );
}
