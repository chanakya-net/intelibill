import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { EXPENSE_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface ExpenseCategoryDto {
  readonly id: string;
  readonly name: string;
}

@Injectable({ providedIn: 'root' })
export class ExpenseCategoryService {
  private readonly http = inject(HttpClient);

  getCategories(): Observable<readonly ExpenseCategoryDto[]> {
    return this.http.get<readonly ExpenseCategoryDto[]>(EXPENSE_ENDPOINTS.categories);
  }
}
