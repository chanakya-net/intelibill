import { salesAdapter, salesReducer } from './sales.reducer';
import { SaleListItemDto } from '../services/sale.service';
import {
  selectAllSales,
  selectErrorMessage,
  selectLastMutationSucceeded,
  selectLastMutationType,
  selectLoadingSales,
  selectReturnPreview,
  selectReturnPreviewErrorMessage,
  selectSelectedSale,
  selectSubmitting,
} from './sales.selectors';

const makeSale = (id: string): SaleListItemDto => ({
  saleId: id,
  invoiceNumber: `INV-${id}`,
  customerId: null,
  paymentMethod: 1,
  soldAt: '2026-04-19T10:00:00Z',
  paidAmount: 500,
  dueAmount: 0,
  totalAmount: 500,
  totalTaxAmount: 50,
  customerName: null,
  customerPhone: null,
  itemCount: 2,
      returnNumbers: [],
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
});
