import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { SaleService } from '../services/sale.service';
import { SalesActions } from './sales.actions';
import { SalesEffects } from './sales.effects';

describe('SalesEffects', () => {
  let actions$: Subject<Action>;
  let effects: SalesEffects;

  const saleService = {
    getSales: vi.fn<SaleService['getSales']>(),
    getSaleById: vi.fn<SaleService['getSaleById']>(),
    recordSale: vi.fn<SaleService['recordSale']>(),
    getProfitLossReport: vi.fn<SaleService['getProfitLossReport']>(),
    previewSaleReturn: vi.fn<SaleService['previewSaleReturn']>(),
  };

  const makeSale = (id = 'sale-1') => ({
    saleId: id,
    invoiceNumber: `INV-${id}`,
    customerId: null,
    paymentMethod: 1,
    soldAt: new Date().toISOString(),
    paidAmount: 500,
    dueAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    customerName: null,
    customerPhone: null,
    itemCount: 1,
    returnNumbers: [],
  });

  const makeSaleDto = (id = 'sale-1') => ({
    saleId: id,
    invoiceNumber: `INV-${id}`,
    customerId: null,
    paymentMethod: 1,
    soldAt: new Date().toISOString(),
    paidAmount: 500,
    dueAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    items: [],
    returns: [],
    warnings: [],
  });

  beforeEach(() => {
    actions$ = new Subject<Action>();
    saleService.getSales.mockReset();
    saleService.getSaleById.mockReset();
    saleService.recordSale.mockReset();
    saleService.getProfitLossReport.mockReset();
    saleService.previewSaleReturn.mockReset();

    TestBed.configureTestingModule({
      providers: [
        SalesEffects,
        { provide: SaleService, useValue: saleService },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(SalesEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches loadSalesSucceeded on load success', async () => {
    const sales = [makeSale()];
    saleService.getSales.mockReturnValue(of(sales));

    const output = firstValueFrom(effects.loadSales$.pipe(take(1)));
    actions$.next(SalesActions.loadSalesRequested());

    await expect(output).resolves.toEqual(SalesActions.loadSalesSucceeded({ sales }));
  });

  it('dispatches loadSalesFailed on load failure', async () => {
    saleService.getSales.mockReturnValue(throwError(() => ({ error: { detail: 'Server error' } })));

    const output = firstValueFrom(effects.loadSales$.pipe(take(1)));
    actions$.next(SalesActions.loadSalesRequested());

    await expect(output).resolves.toEqual(
      SalesActions.loadSalesFailed({ errorMessage: 'Server error' })
    );
  });

  it('dispatches loadSalesFailed with fallback message when no detail', async () => {
    saleService.getSales.mockReturnValue(throwError(() => ({})));

    const output = firstValueFrom(effects.loadSales$.pipe(take(1)));
    actions$.next(SalesActions.loadSalesRequested());

    await expect(output).resolves.toEqual(
      SalesActions.loadSalesFailed({ errorMessage: 'Failed to load sales' })
    );
  });

  it('dispatches loadSaleDetailSucceeded on detail load success', async () => {
    const sale = makeSaleDto();
    saleService.getSaleById.mockReturnValue(of(sale));

    const output = firstValueFrom(effects.loadSaleDetail$.pipe(take(1)));
    actions$.next(SalesActions.loadSaleDetailRequested({ saleId: 'sale-1' }));

    await expect(output).resolves.toEqual(SalesActions.loadSaleDetailSucceeded({ sale }));
  });

  it('dispatches recordSaleSucceeded on record success', async () => {
    const sale = makeSaleDto();
    saleService.recordSale.mockReturnValue(of(sale));

    const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
    actions$.next(
      SalesActions.recordSaleRequested({
        payload: {
          customerId: null,
          customerName: null,
          customerPhone: null,
          paymentMethod: 1,
          paidAmount: 0,
          dueAmount: 0,
          items: [],
        },
      })
    );

    await expect(output).resolves.toEqual(SalesActions.recordSaleSucceeded({ sale }));
  });

  it('dispatches previewSaleReturnSucceeded on preview success', async () => {
    const preview = {
      saleId: 'sale-1',
      hasFinancialAccess: true,
      lines: [],
      financial: null,
      warnings: [],
    };
    const payload = {
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
      items: [{ saleItemId: 'line-1', quantity: 1, condition: 1 as const, approvedRefundAmount: 100, notes: null }],
    };
    saleService.previewSaleReturn.mockReturnValue(of(preview));

    const output = firstValueFrom(effects.previewSaleReturn$.pipe(take(1)));
    actions$.next(SalesActions.previewSaleReturnRequested({ saleId: 'sale-1', payload }));

    await expect(output).resolves.toEqual(SalesActions.previewSaleReturnSucceeded({ preview }));
  });

  it('dispatches previewSaleReturnFailed on preview failure', async () => {
    const payload = {
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
      items: [{ saleItemId: 'line-1', quantity: 1, condition: 1 as const, approvedRefundAmount: null, notes: null }],
    };
    saleService.previewSaleReturn.mockReturnValue(throwError(() => ({ error: { detail: 'Quantity exceeds remaining' } })));

    const output = firstValueFrom(effects.previewSaleReturn$.pipe(take(1)));
    actions$.next(SalesActions.previewSaleReturnRequested({ saleId: 'sale-1', payload }));

    await expect(output).resolves.toEqual(
      SalesActions.previewSaleReturnFailed({ errorMessage: 'Quantity exceeds remaining' })
    );
  });

  it('dispatches recordSaleFailed on record failure', async () => {
    saleService.recordSale.mockReturnValue(throwError(() => ({ error: { detail: 'Insufficient stock' } })));

    const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
    actions$.next(
      SalesActions.recordSaleRequested({
        payload: {
          customerId: null,
          customerName: null,
          customerPhone: null,
          paymentMethod: 1,
          paidAmount: 0,
          dueAmount: 0,
          items: [],
        },
      })
    );

    await expect(output).resolves.toEqual(
      SalesActions.recordSaleFailed({ errorMessage: 'Insufficient stock' })
    );
  });

  it('dispatches loadProfitLossReportSucceeded on success', async () => {
    const report = [{ saleId: '1' }] as any;
    saleService.getProfitLossReport.mockReturnValue(of(report));

    const output = firstValueFrom(effects.loadProfitLossReport$.pipe(take(1)));
    actions$.next(SalesActions.loadProfitLossReportRequested());

    await expect(output).resolves.toEqual(
      SalesActions.loadProfitLossReportSucceeded({ report })
    );
  });

  it('dispatches loadProfitLossReportFailed on failure', async () => {
    saleService.getProfitLossReport.mockReturnValue(
      throwError(() => ({ error: { detail: 'Fail' } }))
    );

    const output = firstValueFrom(effects.loadProfitLossReport$.pipe(take(1)));
    actions$.next(SalesActions.loadProfitLossReportRequested());

    await expect(output).resolves.toEqual(
      SalesActions.loadProfitLossReportFailed({ errorMessage: 'Fail' })
    );
  });
});
