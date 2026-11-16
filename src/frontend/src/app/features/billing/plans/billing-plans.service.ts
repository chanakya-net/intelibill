import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from '../../../core/auth/auth.constants';

export interface PublishedPlan {
  readonly planId: string;
  readonly versionId: string;
  readonly versionNumber: number;
  readonly kind: 'single-shop' | 'multi-shop';
  readonly name: string;
  readonly monthlyPricePaise: number;
  readonly annualPricePaise: number;
  readonly includedShops: number;
  readonly membersPerShop: number;
  readonly extraShopsOffered: boolean;
  readonly extraShopMonthlyPricePaise: number | null;
  readonly extraShopAnnualPricePaise: number | null;
  readonly customerTermsVersion: string;
  readonly refundPolicyVersion: string;
  readonly eligiblePaidGraceDays: number;
  readonly billingTimeZone: string;
  readonly developmentFixture: boolean;
  readonly trialPolicy: {
    readonly versionNumber: number;
    readonly durationDays: number;
    readonly activeMemberCap: number;
    readonly maxConcurrentTrialShops: number;
    readonly trialTermsVersion: string;
  };
}

export interface BillingPlansPage {
  readonly items: readonly PublishedPlan[];
  readonly nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class BillingPlansService {
  private readonly http = inject(HttpClient);

  list(after: string | null) {
    return this.http.get<BillingPlansPage>(API_BASE_URL + '/billing/plans', { params: after ? { after } : {} });
  }
}
