import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import type {
  InvoiceLeaseDto,
  OfflineSalesSyncRequest,
  OfflineSalesSyncResponseDto,
  ProfitLossReportItemDto,
  SalesHistoryQueryParams,
  SalesHistoryResultDto,
  PreviewSaleRequest,
  PreviewSaleReturnRequest,
  RecordSaleRequest,
  RecordSaleReturnRequest,
  ReserveInvoiceLeaseRequest,
  SaleDto,
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

  getSales(params?: SalesHistoryQueryParams): Observable<SalesHistoryResultDto> {
    let requestParams = new HttpParams();

    if (params?.from) requestParams = requestParams.set('from', params.from);
    if (params?.to) requestParams = requestParams.set('to', params.to);
    if (params?.search) requestParams = requestParams.set('search', params.search);
    if (params?.status) requestParams = requestParams.set('status', params.status);
    if (params?.page !== undefined) requestParams = requestParams.set('page', params.page.toString());
    if (params?.pageSize !== undefined) requestParams = requestParams.set('pageSize', params.pageSize.toString());

    return this.http.get<SalesHistoryResultDto>(SALE_ENDPOINTS.list, { params: requestParams });
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
