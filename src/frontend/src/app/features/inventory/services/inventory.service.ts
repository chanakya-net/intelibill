import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { Observable } from 'rxjs';

import { API_BASE_URL, INVENTORY_ENDPOINTS, ITEM_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface Item {
  readonly id: string;
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
  readonly currentStock: number;
}

export interface AddItemRequest {
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
}

export interface UpdateItemRequest {
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
}

export interface AddInventoryBatchRowRequest {
  readonly clientRowId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly itemDescription: string | null;
  readonly uom: string;
  readonly batchNumber: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly taxIncluded: boolean;
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

export interface ProductDetailsDto {
  readonly description: string;
  readonly uom: string;
  readonly costPrice: number;
  readonly mrp: number;
  readonly salesPrice: number;
  readonly supplierId: string | null;
  readonly supplierName: string | null;
  readonly taxIncluded: boolean | null;
  readonly taxRatePercent: number | null;
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

  getProductDetailsByNameOrBarcode(name: string | undefined, barcode: string | undefined): Observable<ProductDetailsDto> {
    const params = new URLSearchParams();
    if (name) {
      params.append('name', name);
    }
    if (barcode) {
      params.append('barcode', barcode);
    }
    return this.http.get<ProductDetailsDto>(`${ITEM_ENDPOINTS.list}/details?${params.toString()}`);
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
}
