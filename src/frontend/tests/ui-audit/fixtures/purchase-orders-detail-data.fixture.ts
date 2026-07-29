import type {
  PurchaseOrderLine,
  PurchaseOrderReceipt,
} from '../../../src/app/features/purchase-orders/services/purchase-order.service';

export function longPurchaseOrderLines(): readonly PurchaseOrderLine[] {
  return [
    {
      lineId: 'line-1',
      itemId: 'item-1',
      description:
        'Very Long Description: This is a comprehensive line item description that spans multiple words and could potentially wrap in certain layouts.',
      expectedQuantity: 100,
      receivedQuantity: 75,
      remainingQuantity: 25,
      unitCost: 125.5,
      lineTotal: 12550,
    },
    {
      lineId: 'line-2',
      itemId: 'item-2',
      description:
        'Another Extended Line Item With Additional Context Information for Audit Purposes',
      expectedQuantity: 50,
      receivedQuantity: 50,
      remainingQuantity: 0,
      unitCost: 250,
      lineTotal: 12500,
    },
  ];
}

export function denseLinesPurchaseOrder(): readonly PurchaseOrderLine[] {
  return Array.from({ length: 35 }, (_, index) => ({
    lineId: `dense-line-${index + 1}`,
    itemId: `item-${index + 1}`,
    description: `Line Item ${index + 1}: Deterministic product description for dense table pagination audit`,
    expectedQuantity: 10 + (index % 5),
    receivedQuantity: (index % 3) + 1,
    remainingQuantity: (10 + (index % 5)) - ((index % 3) + 1),
    unitCost: 50 + (index * 2.5),
    lineTotal: (10 + (index % 5)) * (50 + (index * 2.5)),
  }));
}

export function purchaseOrderReceiptHistory(): readonly PurchaseOrderReceipt[] {
  return [
    receiptHistoryItem(
      1,
      '2026-07-15T10:30:00.000Z',
      'REF-001',
      'First partial receipt',
      'John Doe',
      50,
      6275,
    ),
    receiptHistoryItem(
      2,
      '2026-07-20T14:00:00.000Z',
      'REF-002',
      'Second partial receipt',
      'Jane Smith',
      25,
      3137.5,
    ),
  ];
}

function receiptHistoryItem(
  sequence: number,
  receivedAt: string,
  referenceNumber: string,
  notes: string,
  receivedByDisplayName: string,
  quantity: number,
  totalPurchaseCost: number,
): PurchaseOrderReceipt {
  const suffix = String(sequence).padStart(3, '0');
  return {
    receiptId: `receipt-${sequence}`,
    receiptNumber: `REC-2026-${suffix}`,
    receivedAt,
    referenceNumber,
    notes,
    receivedByUserId: `user-${sequence}`,
    receivedByDisplayName,
    lines: [
      {
        receiptLineId: `recline-${sequence}`,
        purchaseOrderLineId: 'line-1',
        itemId: 'item-1',
        inventoryBatchId: `batch-${sequence}`,
        batchNumber: `BATCH-2026-${suffix}`,
        batchVoided: false,
        stockTransactionId: `tx-${sequence}`,
        quantity,
        totalPurchaseCost,
        unitCost: 125.5,
        mrp: 150,
        salesPrice: 155,
        taxRatePercent: 10,
        taxIncluded: true,
        purchaseTaxIncluded: false,
      },
    ],
  };
}
