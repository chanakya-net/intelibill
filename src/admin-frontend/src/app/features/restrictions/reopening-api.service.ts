import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import type { ProviderClosure, TermStatus } from '../accounts/account-status-api.service';

export interface ReopeningTarget {
  readonly accountId: string;
  readonly closureId: string;
  readonly shopId: string;
}

export interface ReopeningImpact {
  readonly closureId: string;
  readonly shopId: string;
  readonly shopName: string;
  readonly closedAt: string;
  readonly version: number;
  readonly impactDigest: string;
  readonly coverage: TermStatus | null;
  readonly coverageAdequate: boolean;
  readonly otherActiveClosures: number;
  readonly survivingSuspension: { readonly mode: string; readonly effectiveAt: string | null } | null;
}

export interface ReopeningCommand {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly impactDigest: string;
  readonly reason: string;
  readonly ownerConsentReference: string;
  readonly ownerConsentUserId: string;
  readonly ownerConsentAt: string;
  readonly ownerConsentEvidence: string;
  readonly ownerConsentConfirmed: boolean;
}

export interface ReopeningDecision {
  readonly id: string;
  readonly closureId: string;
  readonly shopId: string;
  readonly reopenedAt: string;
}

@Injectable({ providedIn: 'root' })
export class ReopeningApi {
  private readonly http = inject(HttpClient);

  list(account: string, session: string) {
    return this.http.get<readonly ProviderClosure[]>(`/api/admin/accounts/${account}/closures`,
      { headers: { 'X-Admin-Session': session } });
  }

  preview(target: ReopeningTarget, session: string) {
    return this.post<ReopeningImpact>(this.path(target) + '/preview', {}, session);
  }

  save(target: ReopeningTarget, command: ReopeningCommand, session: string) {
    return this.post<ReopeningDecision>(this.path(target), command, session);
  }

  private path({ accountId, closureId, shopId }: ReopeningTarget) {
    return `/api/admin/accounts/${accountId}/closures/${closureId}/shops/${shopId}/reopening`;
  }

  private post<T>(path: string, body: object, session: string) {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<T>(path, body,
        { headers: { 'X-Admin-Session': session, 'X-Admin-CSRF': requestToken } })),
    );
  }
}

export function reopeningRefusal(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return false;
  const code: unknown = error.error?.code;
  if (error.status === 400) return code === 'Reopening.Invalid';
  if (error.status !== 409) return false;
  return code === 'Reopening.VersionConflict' || code === 'Reopening.CoverageRequired' ||
    code === 'Reopening.ConsentRequired' || code === 'Reopening.AlreadyReopened';
}
