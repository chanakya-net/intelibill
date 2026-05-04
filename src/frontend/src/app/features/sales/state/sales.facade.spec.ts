import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { SaleListItemDto } from '../services/sale.service';
import { SalesActions } from './sales.actions';
import { SalesFacade } from './sales.facade';
import {
  selectAllSales,
  selectErrorMessage,
  selectLastMutationSucceeded,
  selectLastMutationType,
  selectLoadingSaleDetail,
  selectLoadingSales,
  selectLoadingReturnPreview,
  selectReturnPreview,
  selectReturnPreviewErrorMessage,
  selectSelectedSale,
  selectSubmitting,
} from './sales.selectors';

describe('SalesFacade', () => {
  const dispatch = vi.fn();
  const salesSignal = signal<SaleListItemDto[]>([]);
  const boolSignal = signal(false);
  const errorSignal = signal('');
  const returnPreviewErrorSignal = signal('');
  const mutationTypeSignal = signal<'record-sale' | 'record-return' | 'void-return' | null>(null);
  const selectedSaleSignal = signal(null);
  const returnPreviewSignal = signal(null);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectAllSales) return salesSignal;
      if (selector === selectLoadingSales) return boolSignal;
      if (selector === selectSubmitting) return boolSignal;
      if (selector === selectErrorMessage) return errorSignal;
      if (selector === selectLastMutationType) return mutationTypeSignal;
      if (selector === selectLastMutationSucceeded) return boolSignal;
      if (selector === selectSelectedSale) return selectedSaleSignal;
      if (selector === selectLoadingSaleDetail) return boolSignal;
      if (selector === selectReturnPreview) return returnPreviewSignal;
      if (selector === selectLoadingReturnPreview) return boolSignal;
      if (selector === selectReturnPreviewErrorMessage) return returnPreviewErrorSignal;
      return signal(null);
    }),
  };

  let facade: SalesFacade;

  beforeEach(() => {
    dispatch.mockReset();
    TestBed.configureTestingModule({
      providers: [SalesFacade, { provide: Store, useValue: store }],
    });
    facade = TestBed.inject(SalesFacade);
  });

  it('loadSales dispatches loadSalesRequested', () => {
    facade.loadSales();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.loadSalesRequested());
  });

  it('loadSaleDetail dispatches loadSaleDetailRequested', () => {
    facade.loadSaleDetail('s1');
    expect(dispatch).toHaveBeenCalledWith(SalesActions.loadSaleDetailRequested({ saleId: 's1' }));
  });

  it('previewSaleReturn dispatches previewSaleReturnRequested', () => {
    const payload = { dueReductionOverrideAmount: null, dueOverrideReason: null, items: [] };
    facade.previewSaleReturn('s1', payload);
    expect(dispatch).toHaveBeenCalledWith(SalesActions.previewSaleReturnRequested({ saleId: 's1', payload }));
  });

  it('recordSaleReturn dispatches recordSaleReturnRequested', () => {
    const payload = { payoutMethod: 1, dueReductionOverrideAmount: null, dueOverrideReason: null, notes: null, items: [] };
    facade.recordSaleReturn('s1', payload);
    expect(dispatch).toHaveBeenCalledWith(SalesActions.recordSaleReturnRequested({ saleId: 's1', payload }));
  });

  it('voidSaleReturn dispatches voidSaleReturnRequested', () => {
    const payload = { reason: 'Wrong return recorded' };
    facade.voidSaleReturn('s1', 'return-1', payload);
    expect(dispatch).toHaveBeenCalledWith(SalesActions.voidSaleReturnRequested({ saleId: 's1', saleReturnId: 'return-1', payload }));
  });

  it('clearError dispatches clearError', () => {
    facade.clearError();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearError());
  });

  it('clearMutationStatus dispatches clearMutationStatus', () => {
    facade.clearMutationStatus();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearMutationStatus());
  });

  it('clearSaleDetail dispatches clearSaleDetail', () => {
    facade.clearSaleDetail();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearSaleDetail());
  });

  it('clearSaleReturnPreview dispatches clearSaleReturnPreview', () => {
    facade.clearSaleReturnPreview();
    expect(dispatch).toHaveBeenCalledWith(SalesActions.clearSaleReturnPreview());
  });
});
