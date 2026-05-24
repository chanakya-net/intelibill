import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { DASHBOARD_ENDPOINTS } from '../../../core/auth/auth.constants';
export type {
  DashboardAlertDto,
  DashboardDto,
  CustomerDueDto,
  PaymentMixDto,
  PaymentMixTrendPointDto,
  PreviousPeriodSummaryDto,
  ProfitTrendPointDto,
  SalesTrendPointDto,
  StockShortageItemDto,
} from '../models/dashboard-dto';
import type { DashboardDto } from '../models/dashboard-dto';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getDashboard(startDate?: string, endDate?: string): Observable<DashboardDto> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    return this.http.get<DashboardDto>(DASHBOARD_ENDPOINTS.summary, { params });
  }
}
