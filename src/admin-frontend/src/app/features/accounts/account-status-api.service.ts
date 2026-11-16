import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, type Observable, expand, reduce } from 'rxjs';

/** Provider grants that may read access status; Platform Owner and Security Administrator alone may not. */
export const statusReaders = ['support', 'billing', 'licensing-manager'] as const;

export interface TermStatus {
  readonly termId: string;
  readonly state: string;
  readonly evidenceState: 'Complete' | 'Incomplete' | 'NeedsReview';
  readonly basis: string | null;
  readonly billingPeriod: string | null;
  readonly timeZone: string;
  readonly startsAt: string;
  readonly paidThrough: string;
  readonly graceEndsAt: string | null;
  readonly accessEndsAt: string | null;
  readonly nextDeadline: string | null;
  readonly nextPeriodEndsAt: string | null;
  readonly coverageMode: 'Independent' | 'Grouped';
  readonly purchasedCapacity: number;
  readonly memberAllowance: number | null;
  readonly sourceReference: string | null;
  readonly developmentFixture: boolean;
  readonly commercial: { readonly pricePaise: number | null; readonly currency: string | null; readonly planVersionId: string | null } | null;
}

export interface AccountTerm {
  readonly status: TermStatus;
  readonly coveredShops: readonly { readonly shopId: string; readonly shopName: string }[];
  readonly coveredShopCount: number;
}

export interface AccountStatus {
  readonly evaluatedAt: string;
  readonly items: readonly AccountTerm[];
}

interface AccountStatusPage extends AccountStatus {
  readonly nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class AccountStatusApi {
  private readonly http = inject(HttpClient);

  /** Every page of the account's terms in order; a lookup is never silently truncated. */
  read(accountId: string): Observable<AccountStatus> {
    const page = (after: string | null) => this.http.get<AccountStatusPage>(
      `/api/admin/accounts/${encodeURIComponent(accountId)}/status`, { params: after ? { after } : {} });
    return page(null).pipe(
      expand(result => result.nextCursor ? page(result.nextCursor) : EMPTY),
      reduce<AccountStatusPage, AccountStatus>((all, result) => ({
        evaluatedAt: all.evaluatedAt || result.evaluatedAt, items: [...all.items, ...result.items],
      }), { evaluatedAt: '', items: [] }),
    );
  }
}

/** An absolute instant in Asia/Kolkata on a 24-hour clock, independent of the browser's zone. */
export function formatIst(value: string | null): string | null {
  if (!value) return null;
  const parts = new Intl.DateTimeFormat('en-IN', {
    timeZone: 'Asia/Kolkata', day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).formatToParts(new Date(value));
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value ?? '';
  return `${part('day')} ${part('month')} ${part('year')}, ${part('hour')}:${part('minute')} IST`;
}

export const stateLabels: Readonly<Partial<Record<string, string>>> = {
  PendingActivation: 'Not yet active', Scheduled: 'Scheduled', Active: 'Active', Grace: 'In grace period',
  Expired: 'Expired', Suspended: 'Suspended', EvidenceIncomplete: 'Facts incomplete', NeedsReview: 'Needs review',
};

export const basisLabels: Readonly<Record<string, string>> = {
  Paid: 'Paid subscription', Trial: 'Trial', Complimentary: 'Courtesy access', Temporary: 'Temporary access',
  Transition: 'Transition access',
};
