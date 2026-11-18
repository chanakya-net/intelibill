import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';

export interface SuspensionSelection {
  readonly scope: 'Account' | 'Shop';
  readonly shopId: string | null;
}

export interface SuspensionImpact {
  readonly accountId: string;
  readonly accountName: string;
  readonly selection: SuspensionSelection;
  readonly version: number;
  readonly alreadySuspended: boolean;
  readonly impactDigest: string;
  readonly shops: readonly {
    readonly shopId: string;
    readonly shopName: string;
    readonly coverageMode: string | null;
  }[];
}

export interface SuspensionCommand extends SuspensionSelection {
  readonly operationKey: string;
  readonly mode: 'Trading' | 'Security';
  readonly effectiveAt: string | null;
  readonly customerExplanation: string;
  readonly internalReason: string;
  readonly reviewReference: string;
  readonly expectedVersion: number;
  readonly impactDigest: string;
}

export interface SuspensionStatus {
  readonly id: string;
  readonly shopId: string | null;
  readonly mode: SuspensionCommand['mode'];
  readonly effectiveAt: string;
  readonly recordedAt: string;
  readonly customerExplanation: string;
  readonly propagation: 'Pending' | 'Acknowledged' | 'NeedsReview' | 'NoShops';
  readonly shops: readonly (SuspensionImpact['shops'][number] & {
    readonly accountRevision: number;
    readonly shopRevision: number;
    readonly status: 'Pending' | 'Acknowledged' | 'NeedsReview';
  })[];
}

@Injectable({ providedIn: 'root' })
export class SuspensionApi {
  private readonly http = inject(HttpClient);

  preview(account: string, selection: SuspensionSelection, session: string) {
    return this.post<SuspensionImpact>(this.path(account) + '/preview', selection, session);
  }

  suspend(account: string, command: SuspensionCommand, session: string) {
    return this.post<SuspensionStatus>(this.path(account), command, session);
  }

  list(account: string, session: string) {
    return this.http.get<SuspensionStatus[]>(this.path(account), { headers: this.headers(session) });
  }

  private post<T>(url: string, body: SuspensionSelection, session: string) {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<T>(url, body,
        { headers: { ...this.headers(session), 'X-Admin-CSRF': requestToken } })),
    );
  }

  private headers(session: string) {
    return { 'X-Admin-Session': session };
  }

  private path(account: string) {
    return `/api/admin/accounts/${encodeURIComponent(account)}/restrictions`;
  }
}
