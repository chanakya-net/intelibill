import { describe, expect, it } from 'vitest';

import type {
  AddInventoryBatchRowRequest,
  AvailableBatchDto,
  BarcodeLabelPrintRequest,
  GenerateItemBarcodeResponse,
} from './inventory.models';

describe('inventory.models', () => {
  it('accepts inventory batch row request shapes', () => {
    const row = {
      clientRowId: 'row-1',
      itemName: 'Rice',
      barcode: '111',
      itemDescription: null,
      hsnCode: '1006',
      uom: 'kg',
      batchNumber: 'BATCH-001',
      quantity: 5,
      totalPurchaseCost: 400,
      mrp: 100,
      salesPrice: 95,
      taxRatePercent: 5,
      taxIncluded: false,
      purchaseTaxIncluded: true,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      referenceNumber: null,
      notes: null,
      performedAt: null,
    } satisfies AddInventoryBatchRowRequest;

    expect(row.purchaseTaxIncluded).toBe(true);
    expect(row.quantity).toBe(5);
  });

  it('accepts available batch dto shapes', () => {
    const batch = {
      barcode: '111',
      itemName: 'Rice',
      batchNumber: 'BATCH-001',
      inventoryBatchId: 'batch-1',
      quantity: 5,
      salesPrice: 95,
      mrp: 100,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
    } satisfies AvailableBatchDto;

    expect(batch.inventoryBatchId).toBe('batch-1');
    expect(batch.taxIncluded).toBe(false);
  });

  it('accepts generate barcode response shape', () => {
    const response: GenerateItemBarcodeResponse = {
      barcode: 'IT-000123',
    };

    expect(response.barcode.startsWith('IT-')).toBe(true);
  });

  it('accepts barcode label request payload shape', () => {
    const request: BarcodeLabelPrintRequest = {
      items: [
        {
          itemId: 'item-1',
          quantity: 4,
          inventoryBatchId: null,
        },
        {
          itemId: 'item-2',
          quantity: 10,
          inventoryBatchId: 'batch-2',
        },
      ],
    };

    expect(request.items).toHaveLength(2);
    expect(request.items[1].inventoryBatchId).toBe('batch-2');
  });
});
