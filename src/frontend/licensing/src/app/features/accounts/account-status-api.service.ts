import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { EntitlementSynchronization } from '@intelibill/api';
import { EMPTY, type Observable, expand, reduce } from 'rxjs';

/** Provider grants that may read access status; Platform Owner and Security Administrator alone may not. */
export const statusReaders = ['support', 'billing', 'licensing-manager'] as const;

export interface CancellationSummary {
  readonly changeId: string;
  readonly version: number;
  readonly effectiveAt: string;
  readonly policyVersion: string;
}

export interface ProviderCancellationDetail extends CancellationSummary {
  readonly accountId: string;
  readonly termId: string;
  readonly affectedShopCount: number;
  readonly affectedShopSetHash: string;
  readonly affectedShops: readonly { readonly shopId: string; readonly shopName: string }[];
  readonly nextCursor: string | null;
}

export interface TermStatus {
  readonly termId: string;
  readonly cancellation?: CancellationSummary;
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
  readonly coveredShops: readonly {
    readonly shopId: string;
    readonly shopName: string;
    readonly synchronization?: EntitlementSynchronization | null;
  }[];
  readonly coveredShopCount: number;
}

export interface AccountStatus {
  readonly evaluatedAt: string;
  readonly items: readonly AccountTerm[];
}

export interface ProviderClosure {
  readonly id: string;
  readonly scope: 'Shop' | 'Account';
  readonly savedAt: string;
  readonly financialTreatment: 'NoFinancialChange';
  readonly shops: readonly {
    readonly shopId: string;
    readonly shopName: string;
    readonly propagation: 'Pending' | 'Applied' | 'NeedsReview' | 'Superseded';
    readonly savedShopRevision: number | null;
    readonly appliedShopRevision: number | null;
    readonly acknowledgedAt: string | null;
    readonly reopening?: {
      readonly id: string;
      readonly reopenedAt: string;
      readonly propagation: 'Pending' | 'Acknowledged' | 'NeedsReview' | 'Superseded';
    };
  }[];
}

interface AccountStatusPage extends AccountStatus {
  readonly nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class AccountStatusApi {
  private readonly http = inject(HttpClient);

  closures(account: string) {
    return this.http.get<readonly ProviderClosure[]>(
      `/api/admin/accounts/${encodeURIComponent(account)}/closures`);
  }

  cancellation(account: string, term: string, after?: string) {
    return this.http.get<ProviderCancellationDetail>(
      `/api/admin/accounts/${encodeURIComponent(account)}/subscriptions/${encodeURIComponent(term)}/cancellation`,
      { params: after ? { after } : {} });
  }

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

/**
 * The exact instant in Asia/Kolkata with seconds and any sub-second digits the server recorded (microseconds are
 * read from the text, since a browser Date keeps only milliseconds). IST has no DST, so fractions are unchanged.
 */
export function formatIstExact(value: string | null): string | null {
  if (!value) return null;
  const parts = new Intl.DateTimeFormat('en-IN', {
    timeZone: 'Asia/Kolkata', day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).formatToParts(new Date(value));
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value ?? '';
  const fraction = /T\d{2}:\d{2}:\d{2}\.(\d+)/.exec(value)?.[1]?.replace(/0+$/, '') ?? '';
  return `${part('day')} ${part('month')} ${part('year')}, ${part('hour')}:${part('minute')}:${part('second')}`
    + `${fraction ? '.' + fraction : ''} IST`;
}

export const stateLabels: Readonly<Partial<Record<string, string>>> = {
  PendingActivation: 'Not yet active', Scheduled: 'Scheduled', Active: 'Active', Grace: 'In grace period',
  Expired: 'Expired', Suspended: 'Suspended', EvidenceIncomplete: 'Facts incomplete', NeedsReview: 'Needs review',
};

export const basisLabels: Readonly<Record<string, string>> = {
  Paid: 'Paid subscription', Trial: 'Trial', Complimentary: 'Courtesy access', Temporary: 'Temporary access',
  Transition: 'Transition access',
};
