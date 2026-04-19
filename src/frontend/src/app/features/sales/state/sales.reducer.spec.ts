import { SalesActions } from './sales.actions';
import { salesReducer, SalesState } from './sales.reducer';
import { SaleListItemDto } from '../services/sale.service';

const makeSale = (id: string, overrides: Partial<SaleListItemDto> = {}): SaleListItemDto => ({
  saleId: id,
  invoiceNumber: `INV-${id}`,
  paymentMethod: 1,
  soldAt: new Date().toISOString(),
  totalAmount: 500,
  totalTaxAmount: 50,
  customerName: null,
  customerPhone: null,
  itemCount: 2,
  ...overrides,
});

describe('salesReducer', () => {
  const initialState = salesReducer(undefined, { type: '@@INIT' } as never);

  it('sets loading state when load sales is requested', () => {
    const next = salesReducer(
      { ...initialState, errorMessage: 'existing error' },
      SalesActions.loadSalesRequested()
    );

    expect(next.loadingSales).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('sets sales when load succeeds', () => {
    const sales = [makeSale('s1'), makeSale('s2')];
    const next = salesReducer(
      { ...initialState, loadingSales: true },
      SalesActions.loadSalesSucceeded({ sales })
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

  it('sets submitting on record sale requested', () => {
    const next = salesReducer(
      initialState,
      SalesActions.recordSaleRequested({
        payload: { customerId: null, customerName: null, customerPhone: null, paymentMethod: 1, items: [] },
      })
    );

    expect(next.submitting).toBe(true);
    expect(next.lastMutationSucceeded).toBe(false);
  });

  it('sets lastMutationSucceeded on record sale succeeded', () => {
    const sale = { saleId: 's1', invoiceNumber: 'INV-s1', paymentMethod: 1, soldAt: '', totalAmount: 0, totalTaxAmount: 0, items: [], warnings: [] };
    const next = salesReducer(
      { ...initialState, submitting: true },
      SalesActions.recordSaleSucceeded({ sale })
    );

    expect(next.submitting).toBe(false);
    expect(next.lastMutationSucceeded).toBe(true);
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
    const sale = { saleId: 's1', invoiceNumber: 'INV', paymentMethod: 1, soldAt: '', totalAmount: 0, totalTaxAmount: 0, items: [], warnings: [] };
    const next = salesReducer(
      { ...initialState, selectedSale: sale },
      SalesActions.clearSaleDetail()
    );

    expect(next.selectedSale).toBeNull();
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
});
