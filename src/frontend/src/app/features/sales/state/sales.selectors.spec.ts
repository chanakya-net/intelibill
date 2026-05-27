import { salesAdapter, salesReducer } from './sales.reducer';
import type { SaleListItemDto } from '../services/sale.models';
import {
  selectAllSales,
  selectErrorMessage,
  selectLastMutationSucceeded,
  selectLastMutationType,
  selectLoadingSales,
  selectReturnPreview,
  selectReturnPreviewErrorMessage,
  selectSalesHistorySummary,
  selectSelectedSale,
  selectSubmitting,
  selectLastRecordedSale,
  selectSalesPagination,
} from './sales.selectors';

const makeSale = (id: string): SaleListItemDto => ({
  saleId: id,
  invoiceNumber: `INV-${id}`,
  customerId: null,
  paymentMethod: 1,
  soldAt: '2026-04-19T10:00:00Z',
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
  status: 'not-returned',
  refundAmount: 0,
  dueReductionAmount: 0,
});

function buildState(sales: SaleListItemDto[] = [], overrides = {}) {
  const base = salesReducer(undefined, { type: '@@INIT' } as never);
  return salesAdapter.setAll(sales, { ...base, ...overrides });
}

describe('sales selectors', () => {
  it('selectAllSales returns all sales sorted by soldAt desc', () => {
    const older = makeSale('s1');
    const newer = { ...makeSale('s2'), soldAt: '2026-04-20T10:00:00Z' };
    const state = buildState([older, newer]);
    const all = selectAllSales.projector(state);
    expect(all[0].saleId).toBe('s2');
    expect(all[1].saleId).toBe('s1');
  });

  it('selectLoadingSales reflects state', () => {
    const state = buildState([], { loadingSales: true });
    expect(selectLoadingSales.projector(state)).toBe(true);
  });

  it('selectSubmitting reflects state', () => {
    const state = buildState([], { submitting: true });
    expect(selectSubmitting.projector(state)).toBe(true);
  });

  it('selectErrorMessage reflects state', () => {
    const state = buildState([], { errorMessage: 'fail' });
    expect(selectErrorMessage.projector(state)).toBe('fail');
  });

  it('selectLastMutationType reflects state', () => {
    const state = buildState([], { lastMutationType: 'record-sale' });
    expect(selectLastMutationType.projector(state)).toBe('record-sale');
  });

  it('selectLastMutationSucceeded reflects state', () => {
    const state = buildState([], { lastMutationSucceeded: true });
    expect(selectLastMutationSucceeded.projector(state)).toBe(true);
  });

  it('selectSelectedSale returns null by default', () => {
    const state = buildState();
    expect(selectSelectedSale.projector(state)).toBeNull();
  });

  it('selectReturnPreview reflects state', () => {
    const preview = { saleId: 's1', hasFinancialAccess: false, lines: [], financial: null, warnings: [] };
    const state = buildState([], { returnPreview: preview });
    expect(selectReturnPreview.projector(state)).toEqual(preview);
  });

  it('selectReturnPreviewErrorMessage reflects state', () => {
    const state = buildState([], { returnPreviewErrorMessage: 'preview failed' });
    expect(selectReturnPreviewErrorMessage.projector(state)).toBe('preview failed');
  });

  it('selectLastRecordedSale reflects state', () => {
    const sale = { saleId: 's1' } as any;
    const state = buildState([], { lastRecordedSale: sale });
    expect(selectLastRecordedSale.projector(state)).toEqual(sale);
  });

  it('selectSalesPagination reflects sales pagination state', () => {
    const state = buildState([], {
      totalCount: 42,
      pageNumber: 3,
      pageSize: 15,
    });

    expect(selectSalesPagination.projector(state)).toEqual({
      totalCount: 42,
      pageNumber: 3,
      pageSize: 15,
    });
  });

  it('selectSalesHistorySummary reflects state', () => {
    const summary = {
      periodSales: 900,
      invoiceCount: 2,
      refundAmount: 40,
    };
    const state = buildState([], { historySummary: summary });
    expect(selectSalesHistorySummary.projector(state)).toEqual(summary);
  });
});
