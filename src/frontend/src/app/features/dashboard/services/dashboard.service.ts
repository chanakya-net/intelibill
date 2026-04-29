import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { DASHBOARD_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface PaymentMixDto {
  readonly cash: number;
  readonly upi: number;
  readonly card: number;
  readonly credit: number;
}

export interface StockShortageItemDto {
  readonly itemName: string;
  readonly quantity: number;
  readonly reorderLevel: number;
  readonly shortage: number;
}

export interface CustomerDueDto {
  readonly customerId: string;
  readonly displayName: string;
  readonly outstandingDue: number;
}

export interface DashboardAlertDto {
  readonly alertType: string;
  readonly priority: number;
}

export interface SalesTrendPointDto {
  readonly date: string;
  readonly amount: number;
}

export interface ProfitTrendPointDto {
  readonly date: string;
  readonly profitAfterTax: number;
}

export interface DashboardDto {
  readonly generatedAt: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly salesCount: number;
  readonly hasNoSalesActivity: boolean;
  readonly salesBooked: number | null;
  readonly cashCollected: number | null;
  readonly profitBeforeTax: number | null;
  readonly profitAfterTax: number | null;
  readonly expenseRecorded: number | null;
  readonly expenseCorrection: number | null;
  readonly netExpense: number | null;
  readonly creditSalesAmount: number | null;
  readonly creditSalesPercentage: number | null;
  readonly paymentMix: PaymentMixDto | null;
  readonly creditShareWarning: boolean | null;
  readonly runningLowStockCount: number;
  readonly criticalStockCount: number;
  readonly rankedShortageList: ReadonlyArray<StockShortageItemDto>;
  readonly highestDueCustomer: CustomerDueDto | null;
  readonly topFiveDueCustomers: ReadonlyArray<CustomerDueDto> | null;
  readonly alerts: ReadonlyArray<DashboardAlertDto>;
  readonly salesTrendSeries: ReadonlyArray<SalesTrendPointDto> | null;
  readonly profitTrendSeries: ReadonlyArray<ProfitTrendPointDto> | null;
}

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

