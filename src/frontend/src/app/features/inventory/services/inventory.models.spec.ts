import { describe, expect, it } from 'vitest';

import type { AddInventoryBatchRowRequest, AvailableBatchDto } from './inventory.models';

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
});
