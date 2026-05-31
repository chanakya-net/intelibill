import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';

import { Observable } from 'rxjs';

import {
  API_BASE_URL,
  ITEM_BARCODE_ENDPOINTS,
  INVENTORY_ENDPOINTS,
  ITEM_ENDPOINTS,
} from '../../../core/auth/auth.constants';
import type {
  AddInventoryBatchRequest,
  AddInventoryBatchResponse,
  AddInventoryBatchRowRequest,
  AdjustInventoryBatchRequest,
  AdjustInventoryBatchResponse,
  AvailableBatchDto,
  HsnLookupResult,
  InventoryAdjustmentHistoryQuery,
  InventoryAdjustmentHistoryResponse,
  InventoryBatchDto,
  InventoryCatalogQuery,
  InventoryCatalogResponse,
  BarcodeLabelPrintRequest,
  GenerateItemBarcodeResponse,
  Item,
  ProductDetailsDto,
  UpdateInventoryBatchRequest,
  UpdateItemRequest,
  VoidInventoryAdjustmentRequest,
  VoidInventoryAdjustmentResponse,
  AddItemRequest,
} from './inventory.models';

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);

  getItems(query: InventoryCatalogQuery): Observable<InventoryCatalogResponse> {
    const params = new HttpParams()
      .set('search', query.search)
      .set('status', query.status)
      .set('pageNumber', query.pageNumber)
      .set('pageSize', query.pageSize);

    return this.http.get<InventoryCatalogResponse>(ITEM_ENDPOINTS.list, { params });
  }

  addItem(payload: AddItemRequest): Observable<Item> {
    return this.http.post<Item>(ITEM_ENDPOINTS.add, payload);
  }

  updateItem(itemId: string, payload: UpdateItemRequest): Observable<void> {
    return this.http.patch<void>(ITEM_ENDPOINTS.update(itemId), payload);
  }

  generateItemBarcode(): Observable<GenerateItemBarcodeResponse> {
    return this.http.post<GenerateItemBarcodeResponse>(ITEM_BARCODE_ENDPOINTS.generate, {});
  }

  printBarcodeLabels(payload: BarcodeLabelPrintRequest): Observable<HttpResponse<Blob>> {
    return this.http.post(ITEM_BARCODE_ENDPOINTS.printLabels, payload, {
      responseType: 'blob',
      observe: 'response',
    }) as Observable<HttpResponse<Blob>>;
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
