import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  API_BASE_URL,
  INVENTORY_ENDPOINTS,
  ITEM_ENDPOINTS,
} from '../../../core/auth/auth.constants';
import { InventoryService } from './inventory.service';

describe('InventoryService', () => {
  function setup(): { service: InventoryService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    return {
      service: TestBed.inject(InventoryService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('sends add item request to items endpoint', () => {
    const { service, http } = setup();

    service
      .addItem({
        name: 'Premium Tea',
        barcode: 'ABC123',
        description: null,
        uom: 'packet',
        isActive: true,
      })
      .subscribe((item) => {
        expect(item.name).toBe('Premium Tea');
      });

    const request = http.expectOne(ITEM_ENDPOINTS.add);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Premium Tea',
      barcode: 'ABC123',
      description: null,
      uom: 'packet',
      isActive: true,
    });

    request.flush({
      id: 'item-1',
      name: 'Premium Tea',
      barcode: 'ABC123',
      description: null,
      uom: 'packet',
      isActive: true,
      currentStock: 0,
    });

    http.verify();
  });

  it('loads items from items endpoint', () => {
    const { service, http } = setup();

    service.getItems().subscribe((items) => {
      expect(items).toHaveLength(1);
      expect(items[0].name).toBe('Premium Tea');
    });

    const request = http.expectOne(ITEM_ENDPOINTS.list);
    expect(request.request.method).toBe('GET');

    request.flush([
      {
        id: 'item-1',
        name: 'Premium Tea',
        barcode: 'ABC123',
        description: null,
        uom: 'packet',
        isActive: true,
        currentStock: 10,
      },
    ]);

    http.verify();
  });

  it('sends update item request to item update endpoint', () => {
    const { service, http } = setup();

    service
      .updateItem('item-1', {
        name: 'Updated Tea',
        barcode: 'ABC999',
        description: 'Updated description',
        uom: 'box',
      })
      .subscribe();

    const request = http.expectOne(ITEM_ENDPOINTS.update('item-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({
      name: 'Updated Tea',
      barcode: 'ABC999',
      description: 'Updated description',
      uom: 'box',
    });

    request.flush(null);

    http.verify();
  });

  it('sends add inventory batch request to inbound batch endpoint', () => {
    const { service, http } = setup();

    service
      .addInventoryBatch({
        items: [
          {
            clientRowId: 'row-1',
            itemName: 'Premium Tea',
            barcode: 'ABC123',
            itemDescription: null,
            uom: 'packet',
            batchNumber: 'BN-1',
            quantity: 5,
            costPrice: 80,
            mrp: 100,
            salesPrice: 95,
            taxRatePercent: 5,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            referenceNumber: null,
            notes: null,
            performedAt: null,
          },
        ],
      })
      .subscribe((response) => {
        expect(response.requestedCount).toBe(1);
        expect(response.successCount).toBe(1);
        expect(response.failedCount).toBe(0);
      });

    const request = http.expectOne(INVENTORY_ENDPOINTS.inboundBatch);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.items).toHaveLength(1);

    request.flush({
      requestedCount: 1,
      successCount: 1,
      failedCount: 0,
      succeeded: [
        {
          clientRowId: 'row-1',
          result: {
            itemId: 'item-1',
            itemName: 'Premium Tea',
            barcode: 'ABC123',
            batchId: 'batch-1',
            batchNumber: 'BN-1',
            batchQuantity: 5,
            totalQuantity: 5,
            supplierId: null,
            stockTransactionId: 'tx-1',
            performedAt: '2026-04-11T00:00:00.000Z',
          },
        },
      ],
      failed: [],
    });

    http.verify();
  });

  it('sends batch adjustment request to batch adjust endpoint', () => {
    const { service, http } = setup();

    service
      .adjustInventoryBatch('batch-1', {
        direction: 'Decrease',
        reason: 'Damaged',
        quantity: 2.5,
        performedAt: '2026-05-05T08:30:00.000Z',
        notes: 'Damaged during handling',
      })
      .subscribe((response) => {
        expect(response.adjustmentNumber).toBe('ADJ-0001');
        expect(response.batchQuantityBefore).toBe(10);
        expect(response.batchQuantityAfter).toBe(7.5);
        expect(response.costImpact).toBe(-250);
      });

    const request = http.expectOne(`${API_BASE_URL}/inventory/batches/batch-1/adjust`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 2.5,
      performedAt: '2026-05-05T08:30:00.000Z',
      notes: 'Damaged during handling',
    });

    request.flush({
      adjustmentId: 'adjustment-1',
      adjustmentNumber: 'ADJ-0001',
      quantity: 2.5,
      unitCost: 100,
      costImpact: -250,
      batchQuantityBefore: 10,
      batchQuantityAfter: 7.5,
      inventoryQuantityBefore: 25,
      inventoryQuantityAfter: 22.5,
      stockTransactionId: 'tx-1',
      performedAt: '2026-05-05T08:30:00.000Z',
    });

    http.verify();
  });
});
