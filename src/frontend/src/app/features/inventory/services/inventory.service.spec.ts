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
            hsnCode: null,
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

  it('lookupHsn_CallsCorrectEndpointWithProductName', () => {
    const { service, http } = setup();

    service.lookupHsn('Milk').subscribe((result) => {
      expect(result.hsnCodes).toHaveLength(1);
    });

    const request = http.expectOne(`${API_BASE_URL}/hsn/lookup`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ productName: 'Milk' });

    request.flush({
      hsnCodes: ['0401'],
      taxScenarios: [{ condition: 'General dairy', taxPercentage: '18%' }],
    });

    http.verify();
  });

  it('lookupHsn_ReturnsHsnCodesAndTaxScenarios', () => {
    const { service, http } = setup();

    service.lookupHsn('Milk').subscribe((result) => {
      expect(result.hsnCodes).toEqual(['0401', '0402']);
      expect(result.taxScenarios).toEqual([
        { condition: 'General dairy', taxPercentage: '18%' },
        { condition: 'Special rate', taxPercentage: '12%' },
      ]);
    });

    const request = http.expectOne(`${API_BASE_URL}/hsn/lookup`);
    expect(request.request.method).toBe('POST');
    request.flush({
      hsnCodes: ['0401', '0402'],
      taxScenarios: [
        { condition: 'General dairy', taxPercentage: '18%' },
        { condition: 'Special rate', taxPercentage: '12%' },
      ],
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

  it('addBatchRow_IncludesHsnCodeInPayload', () => {
    const { service, http } = setup();

    service
      .addInventoryBatch({
        items: [
          {
            clientRowId: 'row-1',
            itemName: 'Premium Tea',
            barcode: 'ABC123',
            itemDescription: null,
            hsnCode: '0401',
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
      .subscribe();

    const request = http.expectOne(INVENTORY_ENDPOINTS.inboundBatch);
    expect(request.request.body.items[0].hsnCode).toBe('0401');

    request.flush({
      requestedCount: 1,
      successCount: 1,
      failedCount: 0,
      succeeded: [],
      failed: [],
    });

    http.verify();
  });

  it('sends void adjustment request to adjustment void endpoint', () => {
    const { service, http } = setup();

    service
      .voidAdjustment('adjustment-1', { reason: 'Duplicate stock count' })
      .subscribe((response) => {
        expect(response.adjustmentId).toBe('adjustment-1');
        expect(response.reversalStockTransactionId).toBe('tx-reversal-1');
      });

    const request = http.expectOne(`${API_BASE_URL}/inventory/adjustments/adjustment-1/void`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'Duplicate stock count' });

    request.flush({
      adjustmentId: 'adjustment-1',
      reversalStockTransactionId: 'tx-reversal-1',
      batchQuantityBefore: 8,
      batchQuantityAfter: 10,
      inventoryQuantityBefore: 18,
      inventoryQuantityAfter: 20,
      voidedAt: '2026-05-05T09:00:00.000Z',
    });

    http.verify();
  });

  it('loads available batches for a search term', () => {
    const { service, http } = setup();

    service.getAvailableBatchesBySearchTerm('rice').subscribe((batches) => {
      expect(batches).toHaveLength(1);
      expect(batches[0].inventoryBatchId).toBe('batch-1');
    });

    const request = http.expectOne(INVENTORY_ENDPOINTS.availableBatches('rice'));
    expect(request.request.method).toBe('GET');

    request.flush([
      {
        barcode: '111',
        itemName: 'Rice',
        batchNumber: 'BATCH-001',
        inventoryBatchId: 'batch-1',
        quantity: 5,
        salesPrice: 100,
        mrp: 120,
        taxRatePercent: 5,
        taxIncluded: false,
        expiryDate: null,
      },
    ]);

    http.verify();
  });

  it('loads adjustment history with server-side filters and paging', () => {
    const { service, http } = setup();

    service
      .getAdjustmentHistory({
        pageNumber: 2,
        pageSize: 25,
        itemId: 'item-1',
        batchId: 'batch-1',
        direction: 'Decrease',
        reason: 'Damaged',
        from: '2026-05-01',
        to: '2026-05-05',
        includeVoided: true,
      })
      .subscribe((response) => {
        expect(response.totalCount).toBe(1);
        expect(response.items[0].adjustmentNumber).toBe('ADJ-0001');
      });

    const request = http.expectOne((req) => req.url === `${API_BASE_URL}/inventory/adjustments`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('pageNumber')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.get('itemId')).toBe('item-1');
    expect(request.request.params.get('batchId')).toBe('batch-1');
    expect(request.request.params.get('direction')).toBe('Decrease');
    expect(request.request.params.get('reason')).toBe('Damaged');
    expect(request.request.params.get('from')).toBe('2026-05-01');
    expect(request.request.params.get('to')).toBe('2026-05-05');
    expect(request.request.params.get('includeVoided')).toBe('true');

    request.flush({
      items: [
        {
          adjustmentId: 'adjustment-1',
          adjustmentNumber: 'ADJ-0001',
          itemId: 'item-1',
          itemName: 'Rice',
          barcode: '111',
          batchId: 'batch-1',
          batchNumber: 'BATCH-001',
          direction: 'Decrease',
          reason: 'Damaged',
          quantity: 2,
          unitCost: 100,
          costImpact: -200,
          batchQuantityBefore: 10,
          batchQuantityAfter: 8,
          inventoryQuantityBefore: 20,
          inventoryQuantityAfter: 18,
          performedAt: '2026-05-05T08:30:00.000Z',
          performedByUserId: 'user-1',
          performedByDisplayName: 'Test User',
          notes: 'Damaged',
          isVoided: false,
          voidedAt: null,
          voidedByUserId: null,
          voidedByDisplayName: null,
          voidReason: null,
          reversalStockTransactionId: null,
        },
      ],
      totalCount: 1,
      pageNumber: 2,
      pageSize: 25,
    });

    http.verify();
  });
});
