import { HttpClient } from '@angular/common/http';
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

export interface DashboardDto {
  readonly generatedAt: string;
  readonly reportingDay: string;
  readonly salesCount: number;
  readonly salesBooked: number;
  readonly cashCollected: number;
  readonly profitBeforeTax: number;
  readonly profitAfterTax: number;
  readonly expenseRecorded: number;
  readonly expenseCorrection: number;
  readonly netExpense: number;
  readonly creditSalesAmount: number;
  readonly creditSalesPercentage: number;
  readonly paymentMix: PaymentMixDto;
  readonly creditShareWarning: boolean;
  readonly runningLowStockCount: number;
  readonly criticalStockCount: number;
  readonly rankedShortageList: ReadonlyArray<StockShortageItemDto>;
  readonly highestDueCustomer: CustomerDueDto | null;
  readonly topFiveDueCustomers: ReadonlyArray<CustomerDueDto>;
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getDashboard(): Observable<DashboardDto> {
    return this.http.get<DashboardDto>(DASHBOARD_ENDPOINTS.summary);
  }
}

