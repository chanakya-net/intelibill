import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import type {
  InvoiceLeaseDto,
  OfflineSalesSyncRequest,
  OfflineSalesSyncResponseDto,
  ProfitLossReportItemDto,
  PreviewSaleRequest,
  PreviewSaleReturnRequest,
  RecordSaleRequest,
  RecordSaleReturnRequest,
  ReserveInvoiceLeaseRequest,
  SaleDto,
  SaleListItemDto,
  SalePreviewDto,
  SaleReturnPreviewDto,
  VoidSaleReturnRequest,
} from './sale.models';

export * from './sale.models';

@Injectable({ providedIn: 'root' })
export class SaleService {
  private readonly http = inject(HttpClient);

  recordSale(request: RecordSaleRequest): Observable<SaleDto> {
    return this.http.post<SaleDto>(SALE_ENDPOINTS.record, request);
  }

  reserveInvoiceLease(request: ReserveInvoiceLeaseRequest): Observable<InvoiceLeaseDto> {
    return this.http.post<InvoiceLeaseDto>(SALE_ENDPOINTS.reserveInvoiceLease, request);
  }

  syncOfflineSales(request: OfflineSalesSyncRequest): Observable<OfflineSalesSyncResponseDto> {
    return this.http.post<OfflineSalesSyncResponseDto>(SALE_ENDPOINTS.offlineSync, request);
  }

  previewSale(request: PreviewSaleRequest): Observable<SalePreviewDto> {
    return this.http.post<SalePreviewDto>(SALE_ENDPOINTS.preview, request);
  }

  getSales(): Observable<readonly SaleListItemDto[]> {
    return this.http.get<readonly SaleListItemDto[]>(SALE_ENDPOINTS.list);
  }

  getSaleById(saleId: string): Observable<SaleDto> {
    return this.http.get<SaleDto>(SALE_ENDPOINTS.detail(saleId));
  }

  previewSaleReturn(saleId: string, request: PreviewSaleReturnRequest): Observable<SaleReturnPreviewDto> {
    return this.http.post<SaleReturnPreviewDto>(`${SALE_ENDPOINTS.detail(saleId)}/returns/preview`, request);
  }

  recordSaleReturn(saleId: string, request: RecordSaleReturnRequest): Observable<SaleDto> {
    return this.http.post<SaleDto>(`${SALE_ENDPOINTS.detail(saleId)}/returns`, request);
  }

  voidSaleReturn(saleReturnId: string, request: VoidSaleReturnRequest): Observable<void> {
    return this.http.post<void>(`${SALE_ENDPOINTS.record}/returns/${saleReturnId}/void`, request);
  }

  getProfitLossReport(): Observable<readonly ProfitLossReportItemDto[]> {
    return this.http.get<readonly ProfitLossReportItemDto[]>(SALE_ENDPOINTS.profitLoss);
  }
}
