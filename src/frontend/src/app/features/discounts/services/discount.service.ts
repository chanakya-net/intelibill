import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { DISCOUNT_ENDPOINTS } from '../../../core/auth/auth.constants';

export type DiscountRuleType = 'BatchPercentage' | 'SalePercentage' | 'SaleThresholdPercentage';

export interface DiscountRuleListItemDto {
  readonly id: string;
  readonly ruleType: DiscountRuleType;
  readonly name: string;
  readonly isActive: boolean;
  readonly startsAt: string | null;
  readonly endsAt: string | null;
  readonly createdAt: string;
}

export interface DiscountRuleDto {
  readonly id: string;
  readonly ruleType: DiscountRuleType;
  readonly name: string;
  readonly description: string | null;
  readonly inventoryBatchId: string | null;
  readonly percentage: number;
  readonly thresholdAmount: number | null;
  readonly startsAt: string | null;
  readonly endsAt: string | null;
  readonly isActive: boolean;
  readonly disabledAt: string | null;
  readonly disabledReason: string | null;
  readonly belowCostConfirmed: boolean;
  readonly belowCostConfirmationReason: string | null;
  readonly replacesRuleId: string | null;
  readonly replacedByRuleId: string | null;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

export interface PaginatedDiscountRules {
  readonly items: readonly DiscountRuleListItemDto[];
  readonly totalCount: number;
  readonly pageNumber: number;
  readonly pageSize: number;
}

export interface GetDiscountRulesParams {
  readonly status?: string;
  readonly ruleType?: DiscountRuleType;
  readonly search?: string;
  readonly sort?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface CreateDiscountRuleRequest {
  readonly ruleType: DiscountRuleType;
  readonly name: string;
  readonly description: string | null;
  readonly inventoryBatchId: string | null;
  readonly percentage: number;
  readonly thresholdAmount: number | null;
  readonly startsAt: string | null;
  readonly endsAt: string | null;
  readonly belowCostConfirmed: boolean;
  readonly belowCostConfirmationReason: string | null;
}

export interface ReplaceDiscountRuleRequest {
  readonly ruleType: DiscountRuleType;
  readonly name: string;
  readonly description: string | null;
  readonly inventoryBatchId: string | null;
  readonly percentage: number;
  readonly thresholdAmount: number | null;
  readonly startsAt: string | null;
  readonly endsAt: string | null;
  readonly belowCostConfirmed: boolean;
  readonly belowCostConfirmationReason: string | null;
  readonly disabledReason: string | null;
}

@Injectable({ providedIn: 'root' })
export class DiscountService {
  private readonly http = inject(HttpClient);

  getDiscountRules(params: GetDiscountRulesParams = {}): Observable<PaginatedDiscountRules> {
    const { status, ruleType, search, sort, page = 1, pageSize = 20 } = params;
    let httpParams = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    if (status) httpParams = httpParams.set('status', status);
    if (ruleType) httpParams = httpParams.set('ruleType', ruleType);
    if (search) httpParams = httpParams.set('search', search);
    if (sort) httpParams = httpParams.set('sort', sort);
    return this.http.get<PaginatedDiscountRules>(DISCOUNT_ENDPOINTS.list, { params: httpParams });
  }

  getDiscountRule(id: string): Observable<DiscountRuleDto> {
    return this.http.get<DiscountRuleDto>(DISCOUNT_ENDPOINTS.detail(id));
  }

  createDiscountRule(request: CreateDiscountRuleRequest): Observable<DiscountRuleDto> {
    return this.http.post<DiscountRuleDto>(DISCOUNT_ENDPOINTS.create, request);
  }

  replaceDiscountRule(id: string, request: ReplaceDiscountRuleRequest): Observable<DiscountRuleDto> {
    return this.http.put<DiscountRuleDto>(DISCOUNT_ENDPOINTS.detail(id), request);
  }

  disableDiscountRule(id: string, reason: string | null): Observable<DiscountRuleDto> {
    return this.http.post<DiscountRuleDto>(DISCOUNT_ENDPOINTS.disable(id), { reason });
  }
}
