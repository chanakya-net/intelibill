export interface Item {
  readonly id: string;
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
  readonly currentStock: number;
  readonly unitPrice: number | null;
  readonly currentStockValue: number;
  readonly reorderLevel: number;
  readonly stockStatus: ItemStockStatus;
  readonly hsnCode: string | null;
  readonly defaultTaxRatePercent: number;
  readonly defaultTaxIncluded: boolean;
}

export type ItemCatalogStatusFilter = 'all' | 'active' | 'inactive' | 'inStock' | 'runningLow' | 'critical';
export type ItemStockStatus = 'inStock' | 'runningLow' | 'critical' | 'inactive';

export interface InventoryCatalogQuery {
  readonly search: string;
  readonly status: ItemCatalogStatusFilter;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export interface InventoryCatalogSummary {
  readonly totalItems: number;
  readonly activeItems: number;
  readonly inactiveItems: number;
  readonly runningLowStockCount: number;
  readonly criticalStockCount: number;
  readonly totalStockValue: number;
}

export interface InventoryCatalogResponse {
  readonly items: readonly Item[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly summary: InventoryCatalogSummary;
}

export interface AddItemRequest {
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
  readonly hsnCode: string | null;
  readonly defaultTaxRatePercent: number;
}

export interface UpdateItemRequest {
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly hsnCode: string | null;
  readonly defaultTaxRatePercent: number;
}

export interface AddInventoryBatchRowRequest {
  readonly clientRowId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly itemDescription: string | null;
  readonly hsnCode: string | null;
  readonly uom: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly totalPurchaseCost: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded?: boolean;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly supplierId: string | null;
  readonly referenceNumber: string | null;
  readonly notes: string | null;
  readonly performedAt: string | null;
}

export interface AddInventoryBatchRequest {
  readonly items: readonly AddInventoryBatchRowRequest[];
  /** Durable identity of this chunk; resending the same rows under it never posts them twice. */
  readonly operationKey?: string;
}

export interface AddInventoryBatchRowError {
  readonly code: string;
  readonly description: string;
}

export interface AddInventoryBatchSucceededRow {
  readonly clientRowId: string;
  readonly result: {
    readonly itemId: string;
    readonly itemName: string;
    readonly barcode: string;
    readonly batchId: string;
    readonly batchNumber: string;
    readonly batchQuantity: number;
    readonly totalQuantity: number;
    readonly supplierId: string | null;
    readonly stockTransactionId: string;
    readonly performedAt: string;
  };
}

export interface AddInventoryBatchFailedRow {
  readonly clientRowId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly errors: readonly AddInventoryBatchRowError[];
  /** Nothing was saved for the row because of a temporary problem; resending retries it. */
  readonly retryable?: boolean;
}

export interface AddInventoryBatchResponse {
  readonly requestedCount: number;
  readonly successCount: number;
  readonly failedCount: number;
  readonly succeeded: readonly AddInventoryBatchSucceededRow[];
  readonly failed: readonly AddInventoryBatchFailedRow[];
}

export interface InventoryBatchDto {
  readonly id: string;
  readonly shopId: string;
  readonly itemId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly originalQuantity: number;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded?: boolean;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly supplierId: string | null;
  readonly supplierName: string | null;
  readonly isVoided: boolean;
  readonly createdAt: string;
  readonly updatedAt: string | null;
  /** Row version; a stock count is accepted only against the version it was taken at. */
  readonly version: number;
  /** The receipt value recorded for this lot; null when no receipt value exists. */
  readonly receiptValueId: string | null;
  /** `LegacyUnresolved` receipts carry no amounts on record and cannot be cost-corrected. */
  readonly receiptStatus: PurchaseReceiptStatus | null;
  /** Total the supplier payable was first posted with; never changes. */
  readonly receiptOriginalTotalCost: number | null;
  /** Effective total: the original plus every cost correction. */
  readonly receiptTotalCost: number | null;
  /** Receipt row version; a cost correction is accepted only against the version it was made at. */
  readonly receiptVersion: number | null;
}

export type PurchaseReceiptStatus = 'Resolved' | 'LegacyUnresolved';

/** Retail price and lot details only: stock is changed by a count, purchase cost by a receipt correction. */
export interface UpdateBatchPricesRequest {
  /** The batch version the editor was shown; a newer batch answers 409. */
  readonly expectedVersion: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly batchNumber: string | null;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly reason: string | null;
}

export interface UpdateBatchPricesResponse {
  readonly batchId: string;
  readonly version: number;
  readonly priceRevisionId: string | null;
}

export interface CorrectReceiptCostRequest {
  readonly expectedVersion: number;
  readonly revisedTotalCost: number;
  readonly reason: string;
  readonly operationKey: string;
}

export interface CorrectReceiptCostResponse {
  readonly adjustmentId: string;
  readonly receiptValueId: string;
  readonly batchId: string;
  readonly priorTotalCost: number;
  readonly revisedTotalCost: number;
  readonly delta: number;
  readonly supplierLedgerEntryId: string;
  readonly receiptVersion: number;
  readonly performedAt: string;
}

export interface CountInventoryBatchRequest {
  readonly expectedVersion: number;
  readonly countedQuantity: number;
  readonly reason: string;
  readonly operationKey: string;
}

export interface CountInventoryBatchResponse {
  readonly stockCountId: string;
  readonly batchId: string;
  readonly observedQuantity: number;
  readonly countedQuantity: number;
  readonly delta: number;
  readonly adjustmentId: string | null;
  readonly adjustmentNumber: string | null;
  readonly stockTransactionId: string | null;
  readonly batchQuantityAfter: number;
  readonly inventoryQuantityAfter: number;
  readonly batchVersion: number;
  readonly performedAt: string;
}

export type InventoryAdjustmentDirection = 'Increase' | 'Decrease';

export type InventoryAdjustmentReason =
  | 'Damaged'
  | 'Expired'
  | 'Stolen'
  | 'MissingLost'
  | 'StockCountCorrection'
  | 'OtherLoss'
  | 'FoundStock'
  | 'ReturnRestockCorrection'
  | 'OtherGain';

export interface AdjustInventoryBatchRequest {
  readonly direction: InventoryAdjustmentDirection;
  readonly reason: InventoryAdjustmentReason;
  readonly quantity: number;
  readonly performedAt: string | null;
  readonly notes: string | null;
}

export interface AdjustInventoryBatchResponse {
  readonly adjustmentId: string;
  readonly adjustmentNumber: string;
  readonly quantity: number;
  readonly unitCost: number;
  readonly costImpact: number;
  readonly batchQuantityBefore: number;
  readonly batchQuantityAfter: number;
  readonly inventoryQuantityBefore: number;
  readonly inventoryQuantityAfter: number;
  readonly stockTransactionId: string;
  readonly performedAt: string;
}

export interface VoidInventoryAdjustmentRequest {
  readonly reason: string;
}

export interface VoidInventoryAdjustmentResponse {
  readonly adjustmentId: string;
  readonly reversalStockTransactionId: string;
  readonly batchQuantityBefore: number;
  readonly batchQuantityAfter: number;
  readonly inventoryQuantityBefore: number;
  readonly inventoryQuantityAfter: number;
  readonly voidedAt: string;
}

export interface InventoryAdjustmentHistoryItem {
  readonly adjustmentId: string;
  readonly adjustmentNumber: string;
  readonly itemId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly batchId: string;
  readonly batchNumber: string;
  readonly direction: InventoryAdjustmentDirection;
  readonly reason: InventoryAdjustmentReason;
  readonly quantity: number;
  readonly unitCost: number;
  readonly costImpact: number;
  readonly batchQuantityBefore: number;
  readonly batchQuantityAfter: number;
  readonly inventoryQuantityBefore: number;
  readonly inventoryQuantityAfter: number;
  readonly performedAt: string;
  readonly performedByUserId: string;
  readonly performedByDisplayName: string;
  readonly notes: string | null;
  readonly isVoided: boolean;
  readonly voidedAt: string | null;
  readonly voidedByUserId: string | null;
  readonly voidedByDisplayName: string | null;
  readonly voidReason: string | null;
  readonly reversalStockTransactionId: string | null;
}

export interface InventoryAdjustmentHistoryQuery {
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly itemId?: string | null;
  readonly batchId?: string | null;
  readonly direction?: InventoryAdjustmentDirection | null;
  readonly reason?: InventoryAdjustmentReason | null;
  readonly from?: string | null;
  readonly to?: string | null;
  readonly includeVoided?: boolean | null;
}

export interface InventoryAdjustmentHistoryResponse {
  readonly items: readonly InventoryAdjustmentHistoryItem[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export interface ProductDetailsDto {
  readonly name: string;
  readonly description: string;
  readonly uom: string;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly supplierId: string | null;
  readonly supplierName: string | null;
  readonly hsnCode: string | null;
  readonly taxIncluded: boolean | null;
  readonly taxRatePercent: number | null;
  /** Whether `costPrice` (the latest batch's unit cost) includes tax; null when there is no previous batch. */
  readonly purchaseTaxIncluded: boolean | null;
}

export interface HsnTaxScenario {
  readonly condition: string;
  readonly taxPercentage: string;
}

export interface HsnLookupResult {
  readonly hsnCodes: readonly string[];
  readonly taxScenarios: readonly HsnTaxScenario[];
}

export interface GenerateItemBarcodeResponse {
  readonly barcode: string;
}

export interface BarcodeLabelPrintRequestItem {
  readonly itemId: string;
  readonly quantity: number;
  readonly inventoryBatchId: string | null;
}

export interface BarcodeLabelPrintRequest {
  readonly items: readonly BarcodeLabelPrintRequestItem[];
}

export interface InventoryBatchOption {
  readonly id: string;
  readonly label: string;
  readonly itemName: string;
  readonly batchNumber: string;
  readonly barcode: string;
  readonly quantity: number;
}

export interface AdjustmentRowDto {
  readonly batchId: string;
  readonly direction: InventoryAdjustmentDirection;
  readonly reason: InventoryAdjustmentReason;
  readonly quantity: number;
  readonly performedAt: string | null;
  readonly notes: string | null;
}

export interface AvailableBatchDto {
  readonly barcode: string;
  readonly itemName: string;
  readonly batchNumber: string;
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly costPrice?: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded?: boolean;
  readonly hsnCode?: string | null;
  readonly expiryDate: string | null;
  readonly lineType?: 'goods' | 'service';
  readonly serviceId?: string | null;
}

export type BatchStatusFilter = 'all' | 'active' | 'voided';

export interface BatchFilters {
  readonly search: string;
  readonly status: BatchStatusFilter;
  readonly fromDate: string;
  readonly toDate: string;
}
