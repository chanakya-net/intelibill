export interface Item {
  readonly id: string;
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
  readonly currentStock: number;
  readonly unitPrice: number;
  readonly currentStockValue: number;
  readonly reorderLevel: number;
  readonly stockStatus: ItemStockStatus;
  readonly hsnCode: string | null;
  readonly defaultTaxRatePercent: number;
  readonly defaultTaxIncluded: boolean;
}

export type ItemCatalogStatusFilter = 'all' | 'active' | 'inactive' | 'inStock' | 'runningLow' | 'critical';
export type ItemStockStatus = 'inStock' | 'runningLow' | 'critical';

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
}

export interface UpdateInventoryBatchRequest {
  readonly newBatchNumber: string | null;
  readonly quantity: number;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
  readonly supplierId: string | null;
  readonly notes: string | null;
  readonly entryDate: string | null;
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
}

export interface HsnTaxScenario {
  readonly condition: string;
  readonly taxPercentage: string;
}

export interface HsnLookupResult {
  readonly hsnCodes: readonly string[];
  readonly taxScenarios: readonly HsnTaxScenario[];
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
}

export type BatchStatusFilter = 'all' | 'active' | 'voided';

export interface BatchFilters {
  readonly search: string;
  readonly status: BatchStatusFilter;
  readonly fromDate: string;
  readonly toDate: string;
}
