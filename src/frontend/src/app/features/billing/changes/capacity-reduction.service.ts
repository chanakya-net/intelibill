import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { CheckoutPaymentService } from '../checkout/checkout-payment.service';
import { isHash, isUuid, shopSetHash, type CheckoutScope, type Period, type Quote } from '../checkout/checkout.models';

/** Mirror of capacity-reduction/1. The browser renders server-frozen facts and never auto-selects shops. */
export type ReductionState =
  | 'Scheduled' | 'Funded' | 'Applying' | 'Applied' | 'NeedsResolution' | 'NeedsReview';

export interface CreateCapacityReductionPreview {
  readonly operationKey: string;
  readonly targetPlanVersionId: string;
  readonly targetCapacity: number;
  readonly observedCapacity: number;
  readonly observedMemberAllowance: number;
  readonly observedPaidThrough: string;
}

export interface CapacityReductionPreview {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly previewId: string;
  readonly termId: string;
  readonly sourcePlanVersionId: string | null;
  readonly targetPlanId: string;
  readonly targetPlanVersionId: string;
  readonly targetPlanVersion: number;
  readonly targetPlanName: string;
  readonly period: Period;
  readonly priorCapacity: number;
  readonly targetCapacity: number;
  readonly priorMemberAllowance: number;
  readonly targetMemberAllowance: number;
  readonly targetMode: 'Independent' | 'Grouped';
  readonly slotsRemoved: number;
  readonly unusedSlots: number;
  readonly minimumShopsToRemove: number;
  readonly sourceShopCount: number;
  readonly sourceShopHash: string;
  readonly sourceHash: string;
  readonly observationDigest: string;
  readonly hasMemberConflicts: boolean;
  readonly conflictedShopCount: number;
  readonly amountPaise: number;
  readonly currency: 'INR';
  readonly priceBasis: 'TaxInclusiveFinal';
  readonly customerTermsVersion: string;
  readonly refundPolicyVersion: string;
  readonly billingTimeZone: 'Asia/Kolkata';
  readonly calendarVersion: 'calendar-month-year/1';
  readonly renewalPolicyVersion: 'renewal/1';
  readonly anchorLocal: string;
  readonly ordinal: number;
  readonly currentStartsAt: string;
  readonly currentPaidThrough: string;
  readonly renewalStartsAt: string;
  readonly renewalEndsAt: string;
  readonly suspensionDisclosed: boolean;
  readonly createdAt: string;
  readonly expiresAt: string;
  readonly developmentFixture: boolean;
  readonly snapshotHash: string;
}

export interface CapacityReductionShop {
  readonly shopId: string;
  readonly name: string;
  readonly affiliationRequestId: string;
  readonly activeMembers: number;
  readonly exceedsAllowance: boolean;
  readonly excessMembers: number;
  readonly retained?: boolean;
}

export interface CapacityReductionShopPage {
  readonly accountId: string;
  readonly referenceId: string;
  readonly items: readonly CapacityReductionShop[];
  readonly nextCursor: string | null;
}

export interface ScheduleCapacityReduction {
  readonly operationKey: string;
  readonly previewId: string;
  readonly previewSnapshotHash: string;
  readonly retainedShopIds: readonly string[];
  readonly customerTermsVersion: string;
  readonly refundPolicyVersion: string;
  readonly confirmed: true;
}

export interface ReductionConflict {
  readonly shopId: string;
  readonly shopName: string;
  readonly reason: string;
  readonly activeMembers: number;
  readonly targetAllowance: number;
}

export interface ScheduledCapacityReduction {
  readonly schemaVersion: 1;
  readonly accountId: string;
  readonly scheduleId: string;
  readonly previewId: string;
  readonly termId: string;
  readonly state: ReductionState;
  readonly reason: string | null;
  readonly targetPlanId: string;
  readonly targetPlanVersionId: string;
  readonly targetPlanVersion: number;
  readonly targetPlanName: string;
  readonly period: Period;
  readonly priorCapacity: number;
  readonly targetCapacity: number;
  readonly priorMemberAllowance: number;
  readonly targetMemberAllowance: number;
  readonly targetMode: 'Independent' | 'Grouped';
  readonly sourceShopCount: number;
  readonly sourceShopHash: string;
  readonly retainedShopCount: number;
  readonly retainedShopHash: string;
  readonly sourceHash: string;
  readonly observationDigest: string;
  readonly amountPaise: number;
  readonly currency: 'INR';
  readonly priceBasis: 'TaxInclusiveFinal';
  readonly customerTermsVersion: string;
  readonly refundPolicyVersion: string;
  readonly billingTimeZone: 'Asia/Kolkata';
  readonly calendarVersion: 'calendar-month-year/1';
  readonly renewalPolicyVersion: 'renewal/1';
  readonly anchorLocal: string;
  readonly ordinal: number;
  readonly currentStartsAt: string;
  readonly currentPaidThrough: string;
  readonly effectiveAt: string;
  readonly renewalEndsAt: string;
  readonly resolutionGeneration: number;
  readonly orderId: string | null;
  readonly appliedTermId: string | null;
  readonly suspensionDisclosed: boolean;
  readonly developmentFixture: boolean;
  readonly createdAt: string;
  readonly fundedAt: string | null;
  readonly appliedAt: string | null;
  readonly snapshotHash: string;
  readonly conflicts?: readonly ReductionConflict[] | null;
}

export interface CreateReductionOrder {
  readonly operationKey: string;
  readonly snapshotHash: string;
}

export interface ResolveCapacityReduction {
  readonly operationKey: string;
  readonly expectedGeneration: number;
  readonly snapshotHash: string;
}

const states: readonly ReductionState[] = [
  'Scheduled', 'Funded', 'Applying', 'Applied', 'NeedsResolution', 'NeedsReview',
];

export function matchesPreviewRequest(preview: CapacityReductionPreview, scope: CheckoutScope, term: string,
  command: CreateCapacityReductionPreview): boolean {
  return preview.schemaVersion === 1 && preview.accountId === scope.account && preview.termId === term &&
    isUuid(preview.previewId) && preview.targetPlanVersionId === command.targetPlanVersionId &&
    preview.targetCapacity === command.targetCapacity && preview.priorCapacity === command.observedCapacity &&
    preview.priorMemberAllowance === command.observedMemberAllowance &&
    Date.parse(preview.currentPaidThrough) === Date.parse(command.observedPaidThrough) &&
    Number.isSafeInteger(preview.amountPaise) && preview.amountPaise > 0 && isHash(preview.snapshotHash);
}

export function isValidSchedule(schedule: ScheduledCapacityReduction, scope: CheckoutScope, term: string): boolean {
  return schedule.schemaVersion === 1 && schedule.accountId === scope.account && schedule.termId === term &&
    isUuid(schedule.scheduleId) && states.includes(schedule.state) &&
    Number.isSafeInteger(schedule.amountPaise) && schedule.amountPaise > 0 && isHash(schedule.snapshotHash);
}

@Injectable({ providedIn: 'root' })
export class CapacityReductionService {
  private readonly http = inject(HttpClient);
  private readonly payments = inject(CheckoutPaymentService);

  paymentStatus(scope: CheckoutScope, orderId: string) {
    return this.payments.read(scope, orderId);
  }

  createPreview(scope: CheckoutScope, term: string, command: CreateCapacityReductionPreview): Promise<CapacityReductionPreview> {
    return firstValueFrom(this.http.post<CapacityReductionPreview>(this.basePath(scope, term) + '/previews', command,
      { context: scopedRequestContext(scope) }));
  }

  schedule(scope: CheckoutScope, term: string, command: ScheduleCapacityReduction): Promise<ScheduledCapacityReduction> {
    return firstValueFrom(this.http.post<ScheduledCapacityReduction>(this.basePath(scope, term), command,
      { context: scopedRequestContext(scope) }));
  }

  readSchedule(scope: CheckoutScope, term: string): Promise<ScheduledCapacityReduction> {
    return firstValueFrom(this.http.get<ScheduledCapacityReduction>(this.basePath(scope, term),
      { context: scopedRequestContext(scope) }));
  }

  recoverSchedule(scope: CheckoutScope, term: string, operationKey: string): Promise<ScheduledCapacityReduction> {
    return firstValueFrom(this.http.get<ScheduledCapacityReduction>(
      this.basePath(scope, term) + '/operations/' + encodeURIComponent(operationKey),
      { context: scopedRequestContext(scope) }));
  }

  createOrder(scope: CheckoutScope, term: string, scheduleId: string, command: CreateReductionOrder): Promise<Quote> {
    return firstValueFrom(this.http.post<Quote>(
      this.basePath(scope, term) + '/' + encodeURIComponent(scheduleId) + '/order', command,
      { context: scopedRequestContext(scope) }));
  }

  resolve(scope: CheckoutScope, term: string, scheduleId: string, command: ResolveCapacityReduction): Promise<ScheduledCapacityReduction> {
    return firstValueFrom(this.http.post<ScheduledCapacityReduction>(
      this.basePath(scope, term) + '/' + encodeURIComponent(scheduleId) + '/resolve', command,
      { context: scopedRequestContext(scope) }));
  }

  async previewShops(scope: CheckoutScope, preview: CapacityReductionPreview, current: () => boolean): Promise<CapacityReductionShop[]> {
    const url = this.basePath(scope, preview.termId) + '/previews/' + encodeURIComponent(preview.previewId) + '/shops';
    return this.loadShops(scope, url, preview.previewId, preview.sourceShopCount, preview.sourceShopHash, current);
  }

  async scheduleShops(scope: CheckoutScope, schedule: ScheduledCapacityReduction, current: () => boolean): Promise<CapacityReductionShop[]> {
    const url = this.basePath(scope, schedule.termId) + '/' + encodeURIComponent(schedule.scheduleId) + '/shops';
    const shops = await this.loadShops(scope, url, schedule.scheduleId, schedule.sourceShopCount, schedule.sourceShopHash, current);
    const retained = shops.filter(s => s.retained === true);
    if (retained.length !== schedule.retainedShopCount ||
      await shopSetHash(retained.map(s => s.shopId)) !== schedule.retainedShopHash)
      throw new Error('Invalid reduction retained shop set');
    return shops;
  }

  private async loadShops(scope: CheckoutScope, url: string, referenceId: string, expectedCount: number,
    expectedHash: string, current: () => boolean): Promise<CapacityReductionShop[]> {
    const shops: CapacityReductionShop[] = [];
    let after: string | null = null;
    do {
      const page: CapacityReductionShopPage = await firstValueFrom(this.http.get<CapacityReductionShopPage>(url,
        { context: scopedRequestContext(scope), params: after ? { after } : {} }));
      if (!current() || page.accountId !== scope.account || page.referenceId !== referenceId ||
        (page.nextCursor && after && page.nextCursor <= after))
        throw new Error('Invalid capacity reduction shop page');
      shops.push(...page.items);
      after = page.nextCursor;
    } while (after && shops.length <= expectedCount);
    if (shops.length !== expectedCount || await shopSetHash(shops.map(s => s.shopId)) !== expectedHash)
      throw new Error('Invalid capacity reduction shop set');
    return shops;
  }

  private basePath(scope: CheckoutScope, term: string): string {
    return API_BASE_URL + '/billing/accounts/' + encodeURIComponent(scope.account) + '/subscriptions/' +
      encodeURIComponent(term) + '/capacity-reductions';
  }
}
