import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';

import { NetworkStatusService } from '../../../core/services/network-status.service';
import { CartItem } from './sale-cart-state.service';
import { SalePreviewDto, SaleService } from './sale.service';
import { SalePreviewService } from './sale-preview.service';

describe('SalePreviewService', () => {
  const canReachApi = signal(true);
  const saleService = {
    previewSale: vi.fn<SaleService['previewSale']>(),
  };
  const networkStatus = {
    canReachApi,
  };

  function setup(): SalePreviewService {
    TestBed.configureTestingModule({
      providers: [
        SalePreviewService,
        { provide: SaleService, useValue: saleService },
        { provide: NetworkStatusService, useValue: networkStatus },
      ],
    });

    return TestBed.inject(SalePreviewService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    canReachApi.set(true);
    saleService.previewSale.mockReturnValue(of(makePreview()));
    vi.clearAllMocks();
  });

  it('debounces preview trigger before calling the API', async () => {
    vi.useFakeTimers();
    const service = setup();

    service.refreshOnlinePreview({
      getCart: () => [makeCartItem()],
      getSaleDiscount: () => ({ type: 0, value: 0 }),
    });

    service.triggerPreview();
    expect(saleService.previewSale).not.toHaveBeenCalled();

    vi.advanceTimersByTime(299);
    expect(saleService.previewSale).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    await Promise.resolve();

    expect(saleService.previewSale).toHaveBeenCalledTimes(1);
    expect(service.checkoutPreview()).toEqual(makePreview());
    vi.useRealTimers();
  });

  it('refreshes immediately on server update trigger', async () => {
    const service = setup();

    service.refreshOnServerUpdate({
      getCart: () => [makeCartItem()],
      getSaleDiscount: () => ({ type: 0, value: 0 }),
    });

    service.triggerServerUpdateRefresh();
    await Promise.resolve();

    expect(saleService.previewSale).toHaveBeenCalledTimes(1);
  });

  it('tracks preview request lifecycle and ignores stale responses', () => {
    const service = setup();

    const firstRequest = service.beginPreviewRequest();
    const secondRequest = service.beginPreviewRequest();

    service.finishPreviewRequest(firstRequest, makePreview());
    expect(service.checkoutPreview()).toBeNull();

    service.finishPreviewRequest(secondRequest, makePreview());
    expect(service.checkoutPreview()).toEqual(makePreview());
    expect(service.isPreviewLoading()).toBe(false);
  });

  it('stores preview errors when requests fail', () => {
    const service = setup();
    const requestId = service.beginPreviewRequest();

    service.finishPreviewRequest(requestId, null, { failed: true, errorMessage: 'Preview failed.' });

    expect(service.previewError()).toBe('Preview failed.');
    expect(service.checkoutPreview()).toBeNull();
  });

  it('extracts preview error details from backend payloads', () => {
    const service = setup();
    const detailError = { error: { detail: 'Pricing invalid.' } };
    const fallbackError = { error: { title: 'Oops' } };

    expect(service.extractPreviewErrorMessage(detailError)).toBe('Pricing invalid.');
    expect(service.extractPreviewErrorMessage(fallbackError)).toBe('Oops');
    expect(service.extractPreviewErrorMessage({})).toBe('sales.newSale.previewError');
  });

  function makePreview(): SalePreviewDto {
    return {
      totalAmount: 100,
      totalTaxableAmount: 100,
      totalTaxAmount: 0,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 100,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    };
  }

  function makeCartItem(): CartItem {
    return {
      clientLineKey: 'line-1',
      barcode: 'BC-1',
      itemName: 'Item',
      batchNumber: 'B-1',
      inventoryBatchId: 'batch-1',
      quantity: 1,
      availableQuantity: 5,
      salesPrice: 100,
      mrp: 100,
      taxRatePercent: 18,
      taxIncluded: true,
      costPrice: 50,
      itemDiscountType: 0,
      itemDiscountValue: 0,
      hsnCode: null,
    };
  }
});
