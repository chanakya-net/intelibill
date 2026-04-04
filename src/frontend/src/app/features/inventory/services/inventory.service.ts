import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { Observable } from 'rxjs';

import { ITEM_ENDPOINTS } from '../../../core/auth/auth.constants';

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

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);

  getItems(): Observable<readonly Item[]> {
    return this.http.get<readonly Item[]>(ITEM_ENDPOINTS.list);
  }

  addItem(payload: AddItemRequest): Observable<Item> {
    return this.http.post<Item>(ITEM_ENDPOINTS.add, payload);
  }
}
