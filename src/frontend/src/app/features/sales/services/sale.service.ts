import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';

export type PaymentMethod = 'Cash' | 'UPI' | 'Card' | 'Credit';
export const PAYMENT_METHOD_VALUES: { value: number; label: PaymentMethod }[] = [
  { value: 1, label: 'Cash' },
  { value: 2, label: 'UPI' },
  { value: 3, label: 'Card' },
  { value: 4, label: 'Credit' },
];

export interface RecordSaleItemRequest {
  readonly barcode: string;
  readonly batchNumber: string;
  readonly itemName: string;
  readonly quantity: number;
  readonly costPrice: number;
  readonly salesPrice: number;
  readonly mrp: number;
  readonly taxRatePercent: number;
  readonly isPriceIncludingTax: boolean;
}

export interface RecordSaleRequest {
  readonly customerId: string | null;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly paymentMethod: number;
  readonly items: readonly RecordSaleItemRequest[];
}

export interface SaleItemDto {
  readonly saleItemId: string;
  readonly itemId: string;
  readonly inventoryBatchId: string;
  readonly quantity: number;
  readonly salesPrice: number;
  readonly taxRatePercent: number;
  readonly hasPriceMismatch: boolean;
}

export interface SaleDto {
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly paymentMethod: number;
  readonly soldAt: string;
  readonly totalAmount: number;
  readonly totalTaxAmount: number;
  readonly items: readonly SaleItemDto[];
  readonly warnings: readonly string[];
}

export interface SaleListItemDto {
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly paymentMethod: number;
  readonly soldAt: string;
  readonly totalAmount: number;
  readonly totalTaxAmount: number;
  readonly customerName: string | null;
  readonly customerPhone: string | null;
  readonly itemCount: number;
}

@Injectable({ providedIn: 'root' })
export class SaleService {
  private readonly http = inject(HttpClient);

  recordSale(request: RecordSaleRequest): Observable<SaleDto> {
    return this.http.post<SaleDto>(SALE_ENDPOINTS.record, request);
  }

  getSales(): Observable<readonly SaleListItemDto[]> {
    return this.http.get<readonly SaleListItemDto[]>(SALE_ENDPOINTS.list);
  }

  getSaleById(saleId: string): Observable<SaleDto> {
    return this.http.get<SaleDto>(SALE_ENDPOINTS.detail(saleId));
  }
}
