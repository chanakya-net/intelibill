import { SalesActions } from './sales.actions';
import { salesReducer, SalesState } from './sales.reducer';
import type { ProfitLossReportResultDto, SaleListItemDto } from '../services/sale.models';

const makeSale = (id: string, overrides: Partial<SaleListItemDto> = {}): SaleListItemDto => ({
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
  itemCount: 2,
      returnNumbers: [],
  status: 'partiallyPaid',
  refundAmount: 0,
  dueReductionAmount: 0,
  creditNoteAppliedAmount: 0,
  ...overrides,
}) as SaleListItemDto;

describe('salesReducer', () => {
  const initialState = salesReducer(undefined, { type: '@@INIT' } as never);

  it('sets loading state when load sales is requested', () => {
    const next = salesReducer(
      { ...initialState, errorMessage: 'existing error' },
      SalesActions.loadSalesRequested({})
    );

    expect(next.loadingSales).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets sales when load succeeds', () => {
    const sales = [makeSale('s1'), makeSale('s2')];
    const next = salesReducer(
      { ...initialState, loadingSales: true },
      SalesActions.loadSalesSucceeded({
        sales,
        totalCount: 2,
        pageNumber: 3,
        pageSize: 25,
        summary: {
          periodSales: 1200,
          invoiceCount: 2,
          refundAmount: 50,
        },
      })
    );

    expect(next.loadingSales).toBe(false);
    expect(next.ids).toContain('s1');
    expect(next.ids).toContain('s2');
  });

  it('sets error when load fails', () => {
    const next = salesReducer(
      { ...initialState, loadingSales: true },
      SalesActions.loadSalesFailed({ errorMessage: 'Failed to load sales' })
    );

    expect(next.loadingSales).toBe(false);
    expect(next.errorMessage).toBe('Failed to load sales');
  });

  it('stores pagination metadata and summary on load success', () => {
    const sales = [makeSale('s1')];
    const summary = {
      periodSales: 995,
      invoiceCount: 1,
      refundAmount: 10,
    };
    const next = salesReducer(
      { ...initialState, loadingSales: true },
      SalesActions.loadSalesSucceeded({
        sales,
        totalCount: 12,
        pageNumber: 2,
        pageSize: 20,
        summary,
      })
    );

    expect(next.totalCount).toBe(12);
    expect(next.pageNumber).toBe(2);
    expect(next.pageSize).toBe(20);
    expect(next.historySummary).toEqual(summary);
  });

  it('sets submitting on record sale requested', () => {
    const next = salesReducer(
      initialState,
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

    expect(next.submitting).toBe(true);
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets lastMutationSucceeded on record sale succeeded', () => {
    const sale = {
      saleId: 's1',
      invoiceNumber: 'INV-s1',
      customerId: null,
      customerName: null,
      customerPhone: null,
      paymentMethod: 1,
      soldAt: '',
      paidAmount: 0,
      dueAmount: 0,
      totalBeforeDiscount: 0,
      totalDiscountAmount: 0,
      totalAmount: 0,
      totalTaxAmount: 0,
      creditNoteAppliedAmount: 0,
      items: [],
      returns: [],
      warnings: [],
    };
    const next = salesReducer(
      { ...initialState, submitting: true },
      SalesActions.recordSaleSucceeded({ sale })
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationSucceeded).toBe(true);
    expect(next.lastRecordedSale).toEqual(sale);
  });

  it('clears last recorded sale on clearLastRecordedSale', () => {
    const sale = { saleId: 's1' } as any;
    const next = salesReducer(
      { ...initialState, lastRecordedSale: sale },
      SalesActions.clearLastRecordedSale()
    );

    expect(next.lastRecordedSale).toBeNull();
  });

  it('sets error on record sale failed', () => {
    const next = salesReducer(
      { ...initialState, submitting: true },
      SalesActions.recordSaleFailed({ errorMessage: 'Failed to record sale' })
    );

    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('Failed to record sale');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('clears selected sale on clearSaleDetail', () => {
    const sale = {
      saleId: 's1',
      invoiceNumber: 'INV',
      customerId: null,
      customerName: null,
      customerPhone: null,
      paymentMethod: 1,
      soldAt: '',
      paidAmount: 0,
      dueAmount: 0,
      totalBeforeDiscount: 0,
      totalDiscountAmount: 0,
      totalAmount: 0,
      totalTaxAmount: 0,
      creditNoteAppliedAmount: 0,
      items: [],
      returns: [],
      warnings: [],
    };
    const next = salesReducer(
      { ...initialState, selectedSale: sale },
      SalesActions.clearSaleDetail()
    );

    expect(next.selectedSale).toBeNull();
  });

  it('sets loading state when return preview is requested', () => {
    const next = salesReducer(
      { ...initialState, returnPreviewErrorMessage: 'old error' },
      SalesActions.previewSaleReturnRequested({
        saleId: 's1',
        payload: { dueReductionOverrideAmount: null, dueOverrideReason: null, items: [] },
      })
    );

    expect(next.loadingReturnPreview).toBe(true);
    expect(next.returnPreview).toBeNull();
    expect(next.returnPreviewErrorMessage).toBe('');
  });

  it('stores return preview when preview succeeds', () => {
    const preview = {
      saleId: 's1',
      hasFinancialAccess: true,
      lines: [],
      financial: null,
      warnings: [],
    };
    const next = salesReducer(
      { ...initialState, loadingReturnPreview: true },
      SalesActions.previewSaleReturnSucceeded({ preview })
    );

    expect(next.loadingReturnPreview).toBe(false);
    expect(next.returnPreview).toEqual(preview);
  });

  it('sets return preview error when preview fails', () => {
    const next = salesReducer(
      { ...initialState, loadingReturnPreview: true },
      SalesActions.previewSaleReturnFailed({ errorMessage: 'Failed' })
    );

    expect(next.loadingReturnPreview).toBe(false);
    expect(next.returnPreviewErrorMessage).toBe('Failed');
  });

  it('updates selected sale when record return succeeds', () => {
    const sale = {
      saleId: 's1',
      invoiceNumber: 'INV-s1',
      customerId: null,
      customerName: null,
      customerPhone: null,
      paymentMethod: 1,
      soldAt: '',
      paidAmount: 0,
      dueAmount: 0,
      totalBeforeDiscount: 0,
      totalDiscountAmount: 0,
      totalAmount: 0,
      totalTaxAmount: 0,
      creditNoteAppliedAmount: 0,
      items: [],
      returns: [],
      warnings: [],
    };
    const next = salesReducer(
      { ...initialState, submitting: true, returnPreview: {} as any },
      SalesActions.recordSaleReturnSucceeded({ sale })
    );

    expect(next.submitting).toBe(false);
    expect(next.selectedSale).toEqual(sale);
    expect(next.returnPreview).toBeNull();
    expect(next.lastMutationType).toBe('record-return');
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('stores record return failures in return preview error without clearing form state', () => {
    const preview = { saleId: 's1', hasFinancialAccess: true, lines: [], financial: null, warnings: [] };
    const next = salesReducer(
      { ...initialState, submitting: true, returnPreview: preview as any },
      SalesActions.recordSaleReturnFailed({ errorMessage: 'Failed return' })
    );

    expect(next.submitting).toBe(false);
    expect(next.returnPreview).toEqual(preview);
    expect(next.returnPreviewErrorMessage).toBe('Failed return');
    expect(next.lastMutationType).toBe('record-return');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets submitting state when void return is requested', () => {
    const next = salesReducer(
      { ...initialState, returnPreviewErrorMessage: 'old error' },
      SalesActions.voidSaleReturnRequested({
        saleId: 's1',
        saleReturnId: 'return-1',
        payload: { reason: 'Wrong return recorded' },
      })
    );

    expect(next.submitting).toBe(true);
    expect(next.returnPreviewErrorMessage).toBe('');
    expect(next.lastMutationType).toBe('void-return');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('updates selected sale when void return succeeds', () => {
    const sale = {
      saleId: 's1',
      invoiceNumber: 'INV-s1',
      customerId: null,
      customerName: null,
      customerPhone: null,
      paymentMethod: 1,
      soldAt: '',
      paidAmount: 0,
      dueAmount: 0,
      totalBeforeDiscount: 0,
      totalDiscountAmount: 0,
      totalAmount: 0,
      totalTaxAmount: 0,
      creditNoteAppliedAmount: 0,
      items: [],
      returns: [],
      warnings: [],
    };
    const next = salesReducer(
      { ...initialState, submitting: true, returnPreviewErrorMessage: 'old error' },
      SalesActions.voidSaleReturnSucceeded({ sale })
    );

    expect(next.submitting).toBe(false);
    expect(next.selectedSale).toEqual(sale);
    expect(next.returnPreviewErrorMessage).toBe('');
    expect(next.lastMutationType).toBe('void-return');
    expect(next.lastMutationSucceeded).toBe(true);
  });

  it('stores void return failures without clearing selected sale', () => {
    const sale = {
      saleId: 's1',
      invoiceNumber: 'INV-s1',
      customerId: null,
      customerName: null,
      customerPhone: null,
      paymentMethod: 1,
      soldAt: '',
      paidAmount: 0,
      dueAmount: 0,
      totalBeforeDiscount: 0,
      totalDiscountAmount: 0,
      totalAmount: 0,
      totalTaxAmount: 0,
      creditNoteAppliedAmount: 0,
      items: [],
      returns: [],
      warnings: [],
    };
    const next = salesReducer(
      { ...initialState, submitting: true, selectedSale: sale },
      SalesActions.voidSaleReturnFailed({ errorMessage: 'Insufficient stock to void return' })
    );

    expect(next.submitting).toBe(false);
    expect(next.selectedSale).toEqual(sale);
    expect(next.returnPreviewErrorMessage).toBe('Insufficient stock to void return');
    expect(next.lastMutationType).toBe('void-return');
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('clears error on clearError', () => {
    const next = salesReducer(
      { ...initialState, errorMessage: 'some error' },
      SalesActions.clearError()
    );

    expect(next.errorMessage).toBe('');
  });

  it('clears mutation status on clearMutationStatus', () => {
    const next = salesReducer(
      { ...initialState, lastMutationType: 'record-sale', lastMutationSucceeded: true },
      SalesActions.clearMutationStatus()
    );

    expect(next.lastMutationType).toBeNull();
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets loading state when load profit loss report is requested', () => {
    const next = salesReducer(
      { ...initialState, errorMessage: 'existing error' },
      SalesActions.loadProfitLossReportRequested({})
    );

    expect(next.loadingProfitLossReport).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets report when load succeeds', () => {
    const result: ProfitLossReportResultDto = {
      items: [
        {
          saleId: 's1',
          referenceNumber: 'INV-s1',
          occurredAt: '',
          partyName: null,
          totalCost: 100,
          wastageCost: 0,
          revenueBeforeTax: 120,
          revenueAfterTax: 140,
          profitBeforeTax: 40,
          profitAfterTax: 20,
          marginPercent: 20,
          rowType: 'Sale',
          inventoryAdjustmentId: null,
        },
      ],
      totalCount: 1,
      pageNumber: 4,
      pageSize: 12,
      summary: {
        netProfitAfterTax: 20,
        revenueIncludingTax: 140,
        totalCost: 100,
        averageMarginPercent: 20,
        invoiceCount: 1,
        returnCount: 0,
        adjustmentCount: 0,
      },
      appliedFilters: {
        from: '2026-05-01',
        to: '2026-05-31',
        type: 'all',
        search: null,
        pageNumber: 4,
        pageSize: 12,
      },
    };
    const next = salesReducer(
      { ...initialState, loadingProfitLossReport: true },
      SalesActions.loadProfitLossReportSucceeded({ result })
    );

    expect(next.loadingProfitLossReport).toBe(false);
    expect(next.profitLossItems).toEqual(result.items);
    expect(next.profitLossSummary).toEqual(result.summary);
    expect(next.profitLossAppliedFilters).toEqual(result.appliedFilters);
    expect(next.profitLossTotalCount).toBe(1);
    expect(next.profitLossPageNumber).toBe(4);
    expect(next.profitLossPageSize).toBe(12);
  });

  it('sets error when report load fails', () => {
    const next = salesReducer(
      { ...initialState, loadingProfitLossReport: true },
      SalesActions.loadProfitLossReportFailed({ errorMessage: 'Failed' })
    );

    expect(next.loadingProfitLossReport).toBe(false);
    expect(next.errorMessage).toBe('Failed');
  });
});
