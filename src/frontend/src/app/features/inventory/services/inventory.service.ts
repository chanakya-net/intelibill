import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';

import { Observable } from 'rxjs';

import {
  API_BASE_URL,
  INVENTORY_ENDPOINTS,
  ITEM_ENDPOINTS,
} from '../../../core/auth/auth.constants';

export interface Item {
  readonly id: string;
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
  readonly currentStock: number;
  readonly hsnCode: string | null;
  readonly defaultTaxRatePercent: number;
  readonly defaultTaxIncluded: boolean;
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

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);

  getItems(): Observable<readonly Item[]> {
    return this.http.get<readonly Item[]>(ITEM_ENDPOINTS.list);
  }

  addItem(payload: AddItemRequest): Observable<Item> {
    return this.http.post<Item>(ITEM_ENDPOINTS.add, payload);
  }

  updateItem(itemId: string, payload: UpdateItemRequest): Observable<void> {
    return this.http.patch<void>(ITEM_ENDPOINTS.update(itemId), payload);
  }

  getProductDetailsByNameOrBarcode(
    name: string | undefined,
    barcode: string | undefined,
  ): Observable<ProductDetailsDto> {
    const params = new URLSearchParams();
    if (name) {
      params.append('name', name);
    }
    if (barcode) {
      params.append('barcode', barcode);
    }
    return this.http.get<ProductDetailsDto>(`${ITEM_ENDPOINTS.list}/details?${params.toString()}`);
  }

  lookupHsn(productName: string): Observable<HsnLookupResult> {
    return this.http.post<HsnLookupResult>(`${API_BASE_URL}/hsn/lookup`, { productName });
  }

  addInventoryBatch(payload: AddInventoryBatchRequest): Observable<AddInventoryBatchResponse> {
    return this.http.post<AddInventoryBatchResponse>(INVENTORY_ENDPOINTS.inboundBatch, payload);
  }

  getInventoryBatches(): Observable<readonly InventoryBatchDto[]> {
    return this.http.get<readonly InventoryBatchDto[]>(`${API_BASE_URL}/inventory/batches`);
  }

  updateInventoryBatch(batchId: string, payload: UpdateInventoryBatchRequest): Observable<void> {
    return this.http.put<void>(`${API_BASE_URL}/inventory/batches/${batchId}`, payload);
  }

  voidInventoryBatch(batchId: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}/inventory/batches/${batchId}/void`, {});
  }

  adjustInventoryBatch(
    batchId: string,
    payload: AdjustInventoryBatchRequest,
  ): Observable<AdjustInventoryBatchResponse> {
    return this.http.post<AdjustInventoryBatchResponse>(
      `${API_BASE_URL}/inventory/batches/${batchId}/adjust`,
      payload,
    );
  }

  getAdjustmentHistory(
    query: InventoryAdjustmentHistoryQuery,
  ): Observable<InventoryAdjustmentHistoryResponse> {
    let params = new HttpParams()
      .set('pageNumber', query.pageNumber)
      .set('pageSize', query.pageSize);

    if (query.itemId) params = params.set('itemId', query.itemId);
    if (query.batchId) params = params.set('batchId', query.batchId);
    if (query.direction) params = params.set('direction', query.direction);
    if (query.reason) params = params.set('reason', query.reason);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.includeVoided !== null && query.includeVoided !== undefined) {
      params = params.set('includeVoided', query.includeVoided);
    }

    return this.http.get<InventoryAdjustmentHistoryResponse>(
      `${API_BASE_URL}/inventory/adjustments`,
      { params },
    );
  }

  voidAdjustment(
    adjustmentId: string,
    payload: VoidInventoryAdjustmentRequest,
  ): Observable<VoidInventoryAdjustmentResponse> {
    return this.http.post<VoidInventoryAdjustmentResponse>(
      `${API_BASE_URL}/inventory/adjustments/${adjustmentId}/void`,
      payload,
    );
  }

  getAvailableBatchesBySearchTerm(searchTerm: string): Observable<readonly AvailableBatchDto[]> {
    return this.http.get<readonly AvailableBatchDto[]>(
      INVENTORY_ENDPOINTS.availableBatches(searchTerm),
    );
  }
}

export interface AvailableBatchDto {
  readonly barcode: string;
  readonly itemName: string;
  readonly batchNumber: string;
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
  readonly purchaseTaxIncluded?: boolean;
  readonly expiryDate: string | null;
}
