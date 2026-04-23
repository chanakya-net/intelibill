import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { EXPENSE_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface RecordExpenseRequest {
  readonly categoryName: string;
  readonly amount: number;
  readonly paidTo: string;
  readonly description: string | null;
  readonly expenseDate: string; // ISO date string
}

export interface ExpenseDto {
  readonly id: string;
  readonly shopId: string;
  readonly categoryId: string;
  readonly categoryName: string;
  readonly amount: number;
  readonly paidTo: string;
  readonly description: string | null;
  readonly expenseDate: string;
  readonly actorUserId: string;
  readonly isVoided: boolean;
  readonly originalExpenseId: string | null;
  readonly createdAt: string;
}

export interface ExpenseListItemDto {
  readonly id: string;
  readonly amount: number;
  readonly categoryName: string;
  readonly paidTo: string;
  readonly expenseDate: string;
  readonly isVoided: boolean;
}

export interface PaginatedExpenses {
  readonly items: readonly ExpenseListItemDto[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class ExpenseService {
  private readonly http = inject(HttpClient);

  getExpenses(search?: string, page = 1, pageSize = 20): Observable<PaginatedExpenses> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<PaginatedExpenses>(EXPENSE_ENDPOINTS.list, { params });
  }

  getExpenseById(expenseId: string): Observable<ExpenseDto> {
    return this.http.get<ExpenseDto>(EXPENSE_ENDPOINTS.detail(expenseId));
  }

  recordExpense(request: RecordExpenseRequest): Observable<ExpenseDto> {
    return this.http.post<ExpenseDto>(EXPENSE_ENDPOINTS.record, request);
  }

  correctExpense(expenseId: string, request: RecordExpenseRequest): Observable<ExpenseDto> {
    return this.http.post<ExpenseDto>(EXPENSE_ENDPOINTS.correct(expenseId), request);
  }
}
