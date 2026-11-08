import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE_URL, SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { ScopeOwner } from '../../../core/context/shop-scope';
import type {
  CancelSaleRequest,
  SaleCancellationDto,
  SaleCancellationPreviewDto,
} from './sale-cancellation.models';
@Injectable({ providedIn: 'root' })
export class SaleCancellationService {
  private readonly http = inject(HttpClient);
  preview(saleId: string) {
    return this.http.post<SaleCancellationPreviewDto>(
      SALE_ENDPOINTS.cancellationPreview(saleId),
      null,
    );
  }
  cancel(saleId: string, request: CancelSaleRequest, owner: ScopeOwner) {
    return this.http.post<SaleCancellationDto>(SALE_ENDPOINTS.cancellations(saleId), request, {
      context: scopedRequestContext(owner),
    });
  }
  getOperation(key: string, owner: ScopeOwner) {
    return this.http.get<{ response: SaleCancellationDto }>(
      `${API_BASE_URL}/financial-operations/sale-cancellation/${encodeURIComponent(key)}`,
      { context: scopedRequestContext(owner) },
    );
  }
}
