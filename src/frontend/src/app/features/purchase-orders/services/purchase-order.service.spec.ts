import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { vi } from 'vitest';
import { of } from 'rxjs';
import { firstValueFrom } from 'rxjs';

import {
  PurchaseOrderListItem,
  PurchaseOrderListResult,
  PurchaseOrderService,
} from './purchase-order.service';

describe('PurchaseOrderService', () => {
  const http = {
    get: vi.fn(),
    post: vi.fn(),
  };

  beforeEach(() => {
    http.get.mockReset();
    http.post.mockReset();

    TestBed.configureTestingModule({
      providers: [
        PurchaseOrderService,
        { provide: HttpClient, useValue: http },
      ],
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('getPurchaseOrders passes filters as query params and returns paged result', async () => {
    const items: readonly PurchaseOrderListItem[] = [
      {
        purchaseOrderId: 'po1',
        purchaseOrderNumber: 'PO-2026-000001',
        status: 'Draft',
        supplierName: null,
        supplierReference: null,
        lineCount: 2,
        expectedQuantity: 5,
        receivedQuantity: 0,
        expectedTotal: 500,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ];
    const response: PurchaseOrderListResult = {
      items,
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
    };
    http.get.mockReturnValue(of(response));

    const service = TestBed.inject(PurchaseOrderService);
    const result = await firstValueFrom(
      service.getPurchaseOrders({
        search: 'rice',
        status: 'Draft',
        orderDateFrom: '2026-06-01',
        orderDateTo: '2026-06-30',
        page: 2,
        pageSize: 50,
      })
    );

    expect(result).toEqual(response);
    expect(http.get).toHaveBeenCalledWith(
      expect.stringContaining('/purchase-orders'),
      expect.objectContaining({
        params: expect.objectContaining({
          get: expect.any(Function),
        }),
      })
    );
  });

  it('getPurchaseOrderDetail calls correct URL', async () => {
    const detail = {
      purchaseOrderId: 'po1',
      purchaseOrderNumber: 'PO-2026-000001',
      status: 'Draft' as const,
      notes: null,
      lines: [],
      expectedTotal: 0,
      createdAt: '2026-06-01T00:00:00Z',
    };
    http.get.mockReturnValue(of(detail));

    const service = TestBed.inject(PurchaseOrderService);
    const result = await firstValueFrom(service.getPurchaseOrderDetail('po1'));

    expect(result).toEqual(detail);
    expect(http.get).toHaveBeenCalledWith(expect.stringContaining('/purchase-orders/po1'));
  });

  it('createDraft calls POST with payload', async () => {
    const detail = {
      purchaseOrderId: 'po2',
      purchaseOrderNumber: 'PO-2026-000002',
      status: 'Draft' as const,
      notes: null,
      lines: [],
      expectedTotal: 0,
      createdAt: '2026-06-01T00:00:00Z',
    };
    http.post.mockReturnValue(of(detail));

    const service = TestBed.inject(PurchaseOrderService);
    const result = await firstValueFrom(service.createDraft({ notes: null, lines: [] }));

    expect(result).toEqual(detail);
    expect(http.post).toHaveBeenCalledWith(
      expect.stringContaining('/purchase-orders'),
      { notes: null, lines: [] }
    );
  });
});
