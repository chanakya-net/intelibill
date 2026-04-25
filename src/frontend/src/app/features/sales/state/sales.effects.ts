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
      switchMap(() =>
        this.saleService.getSales().pipe(
          map((sales) => SalesActions.loadSalesSucceeded({ sales })),
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
