import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { Observable } from 'rxjs';

import { INVENTORY_ENDPOINTS, ITEM_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface Item {
  readonly itemId: string;
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
  readonly preferredSupplierId: string | null;
}

export interface AddItemRequest {
  readonly name: string;
  readonly barcode: string;
  readonly description: string | null;
  readonly uom: string;
  readonly isActive: boolean;
  readonly preferredSupplierId: string | null;
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
  readonly minSalePrice: number;
  readonly taxRatePercent: number;
  readonly expiryDate: string | null;
  readonly manufacturingDate: string | null;
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

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);

  getItems(): Observable<readonly Item[]> {
    return this.http.get<readonly Item[]>(ITEM_ENDPOINTS.list);
  }

  addItem(payload: AddItemRequest): Observable<Item> {
    return this.http.post<Item>(ITEM_ENDPOINTS.add, payload);
  }

  addInventoryBatch(payload: AddInventoryBatchRequest): Observable<AddInventoryBatchResponse> {
    return this.http.post<AddInventoryBatchResponse>(INVENTORY_ENDPOINTS.inboundBatch, payload);
  }
}
