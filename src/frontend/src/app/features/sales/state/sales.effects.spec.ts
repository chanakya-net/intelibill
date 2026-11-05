import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take, toArray } from 'rxjs/operators';
import { vi } from 'vitest';

import { HttpErrorResponse } from '@angular/common/http';

import { CheckoutIntentService } from '../services/checkout-intent.service';
import { StaleScopeError } from '../../../core/context/scoped-request';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { AppReloadService } from '../../../core/services/app-reload.service';
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
    creditNoteRedemptions: [],
    items: [],
    returns: [],
    warnings: [],
    document: null,
    isLegacyDocument: true,
  });

  const checkoutIntents = {
    claimForSend: vi.fn(),
    send: vi.fn(),
    confirm: vi.fn(),
    reject: vi.fn(),
    markUncertain: vi.fn(),
  };

  const intentOwner = { userId: 'user-1', shopId: 'shop-1' };
  let shopContext: ShopContextService;
  const intentPayload = {
    idempotencyKey: 'intent-key',
    customerId: null,
    customerName: null,
    customerPhone: null,
    paymentMethod: 1,
    paidAmount: 500,
    dueAmount: 0,
    items: [],
    saleDiscount: null,
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    for (const mock of Object.values(checkoutIntents)) mock.mockReset();
    checkoutIntents.claimForSend.mockResolvedValue({ ...intentOwner, intentId: 'intent-1', request: intentPayload });
    checkoutIntents.confirm.mockResolvedValue(null);
    checkoutIntents.reject.mockResolvedValue(null);
    checkoutIntents.markUncertain.mockResolvedValue(null);
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
        { provide: CheckoutIntentService, useValue: checkoutIntents },
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(SalesEffects);
    shopContext = TestBed.inject(ShopContextService);
    shopContext.initialize(intentOwner);
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
      SalesActions.loadSalesFailed({ errorMessage: 'errors.sales.loadFailed' })
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

  it('explains a GST document refusal instead of showing the server text', async () => {
    saleService.recordSale.mockReturnValue(
      throwError(() => ({ error: { title: 'Invoice.TaxProfileRequired', detail: 'Declare the tax profile.' } })),
    );

    const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
    actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload }));

    await expect(output).resolves.toEqual(
      SalesActions.recordSaleFailed({ errorMessage: 'errors.invoice.taxProfileRequired' }),
    );
  });

  it('explains a quantity the server refuses for its unit instead of showing the server text', async () => {
    saleService.recordSale.mockReturnValue(
      throwError(() => ({
        error: { title: 'Sale.QuantityPrecisionExceeded', detail: "Quantity for 'X' is sold in PCS and allows no decimals." },
      })),
    );

    const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
    actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload }));

    await expect(output).resolves.toEqual(
      SalesActions.recordSaleFailed({ errorMessage: 'sales.newSale.quantityPrecisionExceeded' }),
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

  describe('checkout intents', () => {
    it('sends the stored intent and confirms it on success', async () => {
      const sale = makeSaleDto('sale-intent');
      checkoutIntents.send.mockReturnValue(of(sale));

      const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));

      await expect(output).resolves.toEqual(SalesActions.recordSaleSucceeded({ sale, intentId: 'intent-1' }));
      expect(checkoutIntents.claimForSend).toHaveBeenCalledWith('intent-1');
      expect(checkoutIntents.confirm).toHaveBeenCalledWith('intent-1', sale);
      expect(saleService.recordSale).not.toHaveBeenCalled();
    });

    it.each([0, 408, 429, 500, 503])('marks the intent uncertain on HTTP %s', async (status) => {
      checkoutIntents.send.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));

      await expect(output).resolves.toEqual(
        SalesActions.recordSaleUncertain({ intentId: 'intent-1', errorMessage: 'sales.newSale.pendingCheckout.uncertain' }),
      );
      expect(checkoutIntents.markUncertain).toHaveBeenCalledWith('intent-1');
      expect(checkoutIntents.reject).not.toHaveBeenCalled();
    });

    it('rejects the intent only on a definitive refusal', async () => {
      checkoutIntents.send.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: { title: 'Sale.Invalid', detail: 'Bad sale' } })),
      );

      const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));

      await expect(output).resolves.toEqual(
        SalesActions.recordSaleFailed({ intentId: 'intent-1', errorMessage: 'Bad sale' }),
      );
      expect(checkoutIntents.reject).toHaveBeenCalledWith('intent-1', 'Sale.Invalid');
      expect(checkoutIntents.markUncertain).not.toHaveBeenCalled();
    });

    it('explains a GST document refusal of a stored intent', async () => {
      checkoutIntents.send.mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 400, error: { title: 'Invoice.CompositionCannotCollectTax', detail: 'x' } }),
        ),
      );

      const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));

      await expect(output).resolves.toEqual(
        SalesActions.recordSaleFailed({ intentId: 'intent-1', errorMessage: 'errors.invoice.compositionCannotCollectTax' }),
      );
      expect(checkoutIntents.reject).toHaveBeenCalledWith('intent-1', 'Invoice.CompositionCannotCollectTax');
    });

    it('uncertain_checkout_survives_switch: an answer lost to a scope change stays on its intent', async () => {
      const answer = new Subject<ReturnType<typeof makeSaleDto>>();
      checkoutIntents.send.mockReturnValue(answer.asObservable());

      const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(shopContext.hasMutationsInFlight()).toBe(true);

      shopContext.transition({ userId: 'user-1', shopId: 'shop-2' }, 'cross-tab', () => undefined);
      answer.error(new StaleScopeError());

      await expect(output).resolves.toEqual(SalesActions.recordSaleDetached({ intentId: 'intent-1' }));
      expect(checkoutIntents.markUncertain).toHaveBeenCalledWith('intent-1');
      expect(checkoutIntents.confirm).not.toHaveBeenCalled();
      expect(shopContext.hasMutationsInFlight()).toBe(false);
    });

    it('holds a switch until the checkout outcome is stored', async () => {
      const answer = new Subject<ReturnType<typeof makeSaleDto>>();
      checkoutIntents.send.mockReturnValue(answer.asObservable());

      const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));
      await new Promise((resolve) => setTimeout(resolve, 0));

      await expect(shopContext.prepareToLeave()).resolves.toEqual({ ok: false, reason: 'mutation-in-flight' });
      answer.next(makeSaleDto('sale-1'));
      answer.complete();
      await output;
      await expect(shopContext.prepareToLeave()).resolves.toEqual({ ok: true });
    });

    it('does not send when another tab holds the intent', async () => {
      checkoutIntents.claimForSend.mockResolvedValue(null);

      const output = firstValueFrom(effects.recordSale$.pipe(take(1)));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));

      await expect(output).resolves.toEqual(
        SalesActions.recordSaleUncertain({
          intentId: 'intent-1',
          errorMessage: 'sales.newSale.pendingCheckout.inProgressElsewhere',
        }),
      );
      expect(checkoutIntents.send).not.toHaveBeenCalled();
    });

    it('never cancels an in-flight checkout when another is requested', async () => {
      const first = new Subject<ReturnType<typeof makeSaleDto>>();
      const second = makeSaleDto('sale-2');
      checkoutIntents.claimForSend.mockImplementation(async (intentId: string) => ({ ...intentOwner, intentId, request: intentPayload }));
      checkoutIntents.send.mockReturnValueOnce(first.asObservable()).mockReturnValueOnce(of(second));

      const output = firstValueFrom(effects.recordSale$.pipe(take(2), toArray()));
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-1' }));
      await Promise.resolve();
      await Promise.resolve();
      actions$.next(SalesActions.recordSaleRequested({ payload: intentPayload, intentId: 'intent-2' }));
      await new Promise((resolve) => setTimeout(resolve, 0));
      first.next(makeSaleDto('sale-1'));
      first.complete();

      const emitted = await output;
      expect(emitted.map((action) => (action as { intentId?: string }).intentId).sort()).toEqual(['intent-1', 'intent-2']);
    });
  });
});
