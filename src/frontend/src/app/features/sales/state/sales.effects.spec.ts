import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { SaleService } from '../services/sale.service';
import type {
  ProfitLossReportQueryParams,
  ProfitLossReportResultDto,
  SaleListItemDto,
  SalesHistoryQueryParams,
} from '../services/sale.models';
import { SalesActions } from './sales.actions';
import { SalesEffects } from './sales.effects';

describe('SalesEffects', () => {
  let actions$: Subject<Action>;
  let effects: SalesEffects;

  const saleService = {
    getSales: vi.fn<SaleService['getSales']>(),
    getSaleById: vi.fn<SaleService['getSaleById']>(),
    recordSale: vi.fn<SaleService['recordSale']>(),
    recordSaleReturn: vi.fn<SaleService['recordSaleReturn']>(),
    voidSaleReturn: vi.fn<SaleService['voidSaleReturn']>(),
    getProfitLossReport: vi.fn<SaleService['getProfitLossReport']>(),
    previewSaleReturn: vi.fn<SaleService['previewSaleReturn']>(),
  };

  const makeSale = (id = 'sale-1'): SaleListItemDto => ({
    saleId: id,
    invoiceNumber: `INV-${id}`,
    customerId: null,
    paymentMethod: 1,
    soldAt: new Date().toISOString(),
    paidAmount: 500,
    dueAmount: 0,
    totalBeforeDiscount: 500,
    totalDiscountAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    customerName: null,
    customerPhone: null,
    itemCount: 1,
    returnNumbers: [],
    status: 'partiallyPaid',
    refundAmount: 0,
    dueReductionAmount: 0,
    creditNoteAppliedAmount: 0,
  } as SaleListItemDto);

  const makeSaleDto = (id = 'sale-1') => ({
    saleId: id,
    invoiceNumber: `INV-${id}`,
    customerId: null,
    customerName: null,
    customerPhone: null,
    paymentMethod: 1,
    soldAt: new Date().toISOString(),
    paidAmount: 500,
    dueAmount: 0,
    totalBeforeDiscount: 500,
    totalDiscountAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    creditNoteAppliedAmount: 0,
    items: [],
    returns: [],
    warnings: [],
  });

  beforeEach(() => {
    actions$ = new Subject<Action>();
    saleService.getSales.mockReset();
    saleService.getSaleById.mockReset();
    saleService.recordSale.mockReset();
    saleService.recordSaleReturn.mockReset();
    saleService.voidSaleReturn.mockReset();
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
    const queryParams: SalesHistoryQueryParams = {
      from: '2026-05-01',
      to: '2026-05-20',
      search: 'INV',
      status: 'paid',
      page: 1,
      pageSize: 30,
    } as const;
    const sales = [makeSale()];
    saleService.getSales.mockReturnValue(
      of({
        items: sales,
        totalCount: 1,
        pageNumber: 1,
        pageSize: 30,
        summary: {
          periodSales: 1000,
          invoiceCount: 1,
          refundAmount: 0,
        },
      })
    );

    const output = firstValueFrom(effects.loadSales$.pipe(take(1)));
    actions$.next(SalesActions.loadSalesRequested({ queryParams }));

    await expect(output).resolves.toEqual(
      SalesActions.loadSalesSucceeded({
        sales,
        totalCount: 1,
        pageNumber: 1,
        pageSize: 30,
        summary: { periodSales: 1000, invoiceCount: 1, refundAmount: 0 },
      })
    );
    expect(saleService.getSales).toHaveBeenCalledWith(queryParams);
  });

  it('dispatches loadSalesFailed on load failure', async () => {
    saleService.getSales.mockReturnValue(throwError(() => ({ error: { detail: 'Server error' } })));

    const output = firstValueFrom(effects.loadSales$.pipe(take(1)));
    actions$.next(SalesActions.loadSalesRequested({}));

    await expect(output).resolves.toEqual(
      SalesActions.loadSalesFailed({ errorMessage: 'Server error' })
    );
  });

  it('dispatches loadSalesFailed with fallback message when no detail', async () => {
    saleService.getSales.mockReturnValue(throwError(() => ({})));

    const output = firstValueFrom(effects.loadSales$.pipe(take(1)));
    actions$.next(SalesActions.loadSalesRequested({}));

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
          idempotencyKey: 'sale-test-key',
          customerId: null,
          customerName: null,
          customerPhone: null,
          paymentMethod: 1,
          paidAmount: 0,
          dueAmount: 0,
          items: [],
          saleDiscount: null,
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

  it('dispatches recordSaleReturnSucceeded on record return success', async () => {
    const sale = makeSaleDto();
    const payload = {
      payoutDestination: 2,
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
      notes: null,
      items: [{ saleItemId: 'line-1', quantity: 1, condition: 1 as const, approvedRefundAmount: 100, notes: 'Sealed' }],
    };
    saleService.recordSaleReturn.mockReturnValue(of(sale));

    const output = firstValueFrom(effects.recordSaleReturn$.pipe(take(1)));
    actions$.next(SalesActions.recordSaleReturnRequested({ saleId: 'sale-1', payload }));

    await expect(output).resolves.toEqual(SalesActions.recordSaleReturnSucceeded({ sale }));
  });

  it('dispatches recordSaleReturnFailed on record return failure', async () => {
    const payload = {
      payoutDestination: 2,
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
      notes: null,
      items: [{ saleItemId: 'line-1', quantity: 1, condition: 1 as const, approvedRefundAmount: 100, notes: 'Sealed' }],
    };
    saleService.recordSaleReturn.mockReturnValue(throwError(() => ({ error: { detail: 'Insufficient stock to void return' } })));

    const output = firstValueFrom(effects.recordSaleReturn$.pipe(take(1)));
    actions$.next(SalesActions.recordSaleReturnRequested({ saleId: 'sale-1', payload }));

    await expect(output).resolves.toEqual(
      SalesActions.recordSaleReturnFailed({ errorMessage: 'Insufficient stock to void return' })
    );
  });

  it('dispatches voidSaleReturnSucceeded with refreshed sale detail on void success', async () => {
    const sale = makeSaleDto();
    const payload = { reason: 'Wrong return recorded' };
    saleService.voidSaleReturn.mockReturnValue(of(void 0));
    saleService.getSaleById.mockReturnValue(of(sale));

    const output = firstValueFrom(effects.voidSaleReturn$.pipe(take(1)));
    actions$.next(SalesActions.voidSaleReturnRequested({ saleId: 'sale-1', saleReturnId: 'return-1', payload }));

    await expect(output).resolves.toEqual(SalesActions.voidSaleReturnSucceeded({ sale }));
    expect(saleService.voidSaleReturn).toHaveBeenCalledWith('return-1', payload);
    expect(saleService.getSaleById).toHaveBeenCalledWith('sale-1');
  });

  it('dispatches voidSaleReturnFailed with backend detail on void failure', async () => {
    const payload = { reason: 'Wrong return recorded' };
    saleService.voidSaleReturn.mockReturnValue(throwError(() => ({ error: { detail: 'Insufficient stock to void return' } })));

    const output = firstValueFrom(effects.voidSaleReturn$.pipe(take(1)));
    actions$.next(SalesActions.voidSaleReturnRequested({ saleId: 'sale-1', saleReturnId: 'return-1', payload }));

    await expect(output).resolves.toEqual(
      SalesActions.voidSaleReturnFailed({ errorMessage: 'Insufficient stock to void return' })
    );
  });

  it('dispatches recordSaleFailed on record failure', async () => {
    saleService.recordSale.mockReturnValue(throwError(() => ({ error: { detail: 'Insufficient stock' } })));

    const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
    actions$.next(
      SalesActions.recordSaleRequested({
        payload: {
          idempotencyKey: 'sale-test-key',
          customerId: null,
          customerName: null,
          customerPhone: null,
          paymentMethod: 1,
          paidAmount: 0,
          dueAmount: 0,
          items: [],
          saleDiscount: null,
        },
      })
    );

    await expect(output).resolves.toEqual(
      SalesActions.recordSaleFailed({ errorMessage: 'Insufficient stock' })
    );
  });

  it('dispatches loadProfitLossReportSucceeded on success', async () => {
    const queryParams: ProfitLossReportQueryParams = {
      from: '2026-05-01',
      to: '2026-05-31',
      type: 'inventoryAdjustment',
      search: 'INV',
      page: 2,
      pageSize: 10,
    };
    const result: ProfitLossReportResultDto = {
      items: [{
      saleId: '1',
      referenceNumber: 'INV-1',
      occurredAt: '2026-05-05T10:00:00Z',
      partyName: null,
      totalCost: 0,
      wastageCost: 0,
      revenueBeforeTax: 0,
      revenueAfterTax: 0,
      profitBeforeTax: 0,
      profitAfterTax: 0,
      marginPercent: null,
      rowType: 'Sale' as const,
      inventoryAdjustmentId: null,
    }],
      totalCount: 1,
      pageNumber: 2,
      pageSize: 10,
      summary: {
        netProfitAfterTax: 0,
        revenueIncludingTax: 0,
        totalCost: 0,
        averageMarginPercent: null,
        invoiceCount: 1,
        returnCount: 0,
        adjustmentCount: 0,
      },
      appliedFilters: {
        from: '2026-05-01',
        to: '2026-05-31',
        type: 'inventoryAdjustment',
        search: 'INV',
        pageNumber: 2,
        pageSize: 10,
      },
    };
    saleService.getProfitLossReport.mockReturnValue(of(result));

    const output = firstValueFrom(effects.loadProfitLossReport$.pipe(take(1)));
    actions$.next(SalesActions.loadProfitLossReportRequested({ queryParams }));

    await expect(output).resolves.toEqual(
      SalesActions.loadProfitLossReportSucceeded({ result })
    );
    expect(saleService.getProfitLossReport).toHaveBeenCalledWith(queryParams);
  });

  it('dispatches loadProfitLossReportFailed on failure', async () => {
    saleService.getProfitLossReport.mockReturnValue(
      throwError(() => ({ error: { detail: 'Fail' } }))
    );

    const output = firstValueFrom(effects.loadProfitLossReport$.pipe(take(1)));
    actions$.next(SalesActions.loadProfitLossReportRequested({}));

    await expect(output).resolves.toEqual(
      SalesActions.loadProfitLossReportFailed({ errorMessage: 'Fail' })
    );
  });

  it('dispatches loadProfitLossReport with undefined query params when omitted', async () => {
    const result: ProfitLossReportResultDto = {
      items: [],
      summary: {
        netProfitAfterTax: 0,
        revenueIncludingTax: 0,
        totalCost: 0,
        averageMarginPercent: null,
        invoiceCount: 0,
        returnCount: 0,
        adjustmentCount: 0,
      },
      appliedFilters: {
        from: '2026-05-24',
        to: '2026-05-30',
        type: 'all',
        search: null,
        pageNumber: 1,
        pageSize: 20,
      },
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
    };
    saleService.getProfitLossReport.mockReturnValue(of(result));

    const output = firstValueFrom(effects.loadProfitLossReport$.pipe(take(1)));
    actions$.next(SalesActions.loadProfitLossReportRequested({}));

    await expect(output).resolves.toEqual(
      SalesActions.loadProfitLossReportSucceeded({ result })
    );
    expect(saleService.getProfitLossReport).toHaveBeenCalledWith(undefined);
  });
});
