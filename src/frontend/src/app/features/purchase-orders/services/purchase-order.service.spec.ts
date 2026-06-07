import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { vi } from 'vitest';
import { of } from 'rxjs';
import { firstValueFrom } from 'rxjs';

import { PurchaseOrderService, PurchaseOrderListItem } from './purchase-order.service';

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

  it('getPurchaseOrders calls correct URL and returns list', async () => {
    const list: readonly PurchaseOrderListItem[] = [
      {
        purchaseOrderId: 'po1',
        purchaseOrderNumber: 'PO-2026-000001',
        status: 'Draft',
        lineCount: 2,
        expectedTotal: 500,
        createdAt: '2026-06-01T00:00:00Z',
      },
    ];
    http.get.mockReturnValue(of(list));

    const service = TestBed.inject(PurchaseOrderService);
    const result = await firstValueFrom(service.getPurchaseOrders());

    expect(result).toEqual(list);
    expect(http.get).toHaveBeenCalledWith(expect.stringContaining('/purchase-orders'));
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
