import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Observable, switchMap } from 'rxjs';

/** The grant facts an operator enters; instants carry an explicit offset (IST from this console). */
export interface GrantDraft {
  readonly shopIds: readonly string[];
  readonly startsAt: string;
  readonly endsAt: string;
  readonly memberAllowance: number;
  readonly reason: string;
  readonly paymentOrderId?: string;
}

export interface GrantShop {
  readonly shopId: string;
  readonly shopName: string;
  readonly baselineState: string;
  readonly baselineBasis: string | null;
  readonly baselineAccessEndsAt: string | null;
}

export interface GrantPreview extends GrantDraft {
  readonly accountId: string;
  readonly basis: 'Complimentary';
  readonly shops: readonly GrantShop[];
  readonly timeZone: string;
  readonly coverageMode: 'Independent' | 'Grouped';
  readonly shopCapacity: number;
  readonly expectedVersion: number;
  readonly sourceHash: string;
  readonly evaluatedAt: string;
  readonly paymentRecorded: false;
  readonly graceEndsAt: null;
}

/** The exact confirmation body, frozen before it is sent and replayed unchanged on recovery. */
export interface GrantCommand extends GrantDraft {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly sourceHash: string;
  readonly confirmed: true;
}

export interface GrantResult extends GrantDraft {
  readonly operationKey: string;
  readonly grantId: string;
  readonly accountId: string;
  readonly coverageMode: 'Independent' | 'Grouped';
  readonly shopCapacity: number;
  readonly grantedVersion: number;
  readonly recordedAt: string;
  readonly paymentRecorded: false;
  readonly graceEndsAt: null;
}

@Injectable({ providedIn: 'root' })
export class ComplimentaryGrantApi {
  private readonly http = inject(HttpClient);

  preview(accountId: string, draft: GrantDraft, sessionId: string): Observable<GrantPreview> {
    return this.withProof(headers => this.http.post<GrantPreview>(this.path(accountId) + '/preview', draft,
      { headers: { ...headers, 'X-Admin-Session': sessionId } }));
  }

  confirm(accountId: string, command: GrantCommand, sessionId: string): Observable<GrantResult> {
    return this.withProof(headers => this.http.post<GrantResult>(this.path(accountId), command,
      { headers: { ...headers, 'X-Admin-Session': sessionId } }));
  }

  recover(accountId: string, operationKey: string, sessionId: string): Observable<GrantResult> {
    return this.http.get<GrantResult>(`${this.path(accountId)}/operations/${encodeURIComponent(operationKey)}`,
      { headers: { 'X-Admin-Session': sessionId } });
  }

  private path(accountId: string) {
    return `/api/admin/accounts/${encodeURIComponent(accountId)}/grants/complimentary`;
  }

  private withProof<T>(send: (headers: Record<string, string>) => Observable<T>) {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => send({ 'X-Admin-CSRF': requestToken })),
    );
  }
}

/** A local IST date-time (YYYY-MM-DDTHH:mm[:ss]) as an explicit +05:30 instant; anything else is refused. */
export function istInstant(local: string): string | null {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2})(:\d{2})?$/.exec(local.trim());
  return match ? `${match[1]}${match[2] ?? ':00'}+05:30` : null;
}
