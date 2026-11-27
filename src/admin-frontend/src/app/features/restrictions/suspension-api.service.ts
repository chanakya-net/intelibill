import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';

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

export interface ReleaseCommand {
  readonly operationKey: string;
  readonly expectedVersion: number;
  readonly impactDigest: string;
  readonly reason: string;
}

export interface ReleaseImpact {
  readonly restrictionId: string;
  readonly version: number;
  readonly evaluatedAt: string;
  readonly impactDigest: string;
  readonly shops: readonly {
    readonly shopId: string;
    readonly shopName: string;
    readonly state: string;
    readonly basis: string | null;
    readonly paidThrough: string | null;
    readonly graceEndsAt: string | null;
    readonly accessEndsAt: string | null;
    readonly survivingSuspension: { readonly mode: string; readonly effectiveAt: string | null } | null;
  }[];
}

interface SuspensionDelivery {
  readonly shopId: string;
  readonly shopName: string;
  readonly accountRevision: number | null;
  readonly shopRevision: number | null;
  readonly status: 'Pending' | 'Acknowledged' | 'NeedsReview';
}

type Propagation = 'Pending' | 'Acknowledged' | 'NeedsReview' | 'NoShops';

export interface SuspensionStatus {
  readonly id: string;
  readonly shopId: string | null;
  readonly mode: SuspensionCommand['mode'];
  readonly effectiveAt: string;
  readonly recordedAt: string;
  readonly customerExplanation: string;
  readonly propagation: Propagation;
  readonly shops: readonly SuspensionDelivery[];
  readonly release?: {
    readonly releasedAt: string;
    readonly releasedBy: string;
    readonly propagation: Propagation;
    readonly shops: readonly SuspensionDelivery[];
  } | null;
}

export class SuspensionRefusal extends Error {
  constructor(readonly response: HttpErrorResponse) {
    super('The suspension endpoint refused the request.');
  }
}

function definitiveRefusal(error: unknown): error is HttpErrorResponse {
  if (!(error instanceof HttpErrorResponse)) return false;
  switch (error.error?.code) {
    case 'Suspension.Invalid':
    case 'Suspension.InvalidRelease': return error.status === 400;
    case 'Suspension.NotFound': return error.status === 404;
    case 'Suspension.VersionConflict':
    case 'Suspension.AlreadySuspended': return error.status === 409;
    default: return false;
  }
}

@Injectable({ providedIn: 'root' })
export class SuspensionApi {
  private readonly http = inject(HttpClient);

  preview(account: string, selection: SuspensionSelection, session: string) {
    return this.post<SuspensionImpact>(this.path(account) + '/preview', selection, session);
  }

  suspend(account: string, command: SuspensionCommand, session: string) {
    // The saved draft also carries UI state (for example its validity), so send only the command.
    const { scope, shopId, operationKey, mode, effectiveAt, customerExplanation, internalReason,
      reviewReference, expectedVersion, impactDigest } = command;
    const body: SuspensionCommand = { scope, shopId, operationKey, mode, effectiveAt, customerExplanation,
      internalReason, reviewReference, expectedVersion, impactDigest };
    return this.save(this.path(account), body, session);
  }

  previewRelease(account: string, restriction: string, session: string) {
    return this.post<ReleaseImpact>(this.releasePath(account, restriction) + '/preview', {}, session);
  }

  release(account: string, restriction: string, command: ReleaseCommand, session: string) {
    return this.save(this.releasePath(account, restriction), command, session);
  }

  private save(url: string, body: SuspensionCommand | ReleaseCommand, session: string) {
    return this.csrf().pipe(
      catchError((cause: unknown) => throwError(() => new Error('Suspension context unavailable.', { cause }))),
      switchMap(({ requestToken }) => this.http.post<SuspensionStatus>(url, body,
        { headers: { ...this.headers(session), 'X-Admin-CSRF': requestToken } }).pipe(
        catchError((error: unknown) => throwError(() =>
          definitiveRefusal(error) ? new SuspensionRefusal(error) : error)),
      )),
    );
  }

  list(account: string, session: string) {
    return this.http.get<SuspensionStatus[]>(this.path(account), { headers: this.headers(session) });
  }

  private post<T>(url: string, body: object, session: string) {
    return this.csrf().pipe(
      switchMap(({ requestToken }) => this.http.post<T>(url, body,
        { headers: { ...this.headers(session), 'X-Admin-CSRF': requestToken } })),
    );
  }

  private csrf() {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context');
  }

  private headers(session: string) {
    return { 'X-Admin-Session': session };
  }

  private releasePath(account: string, restriction: string) {
    return this.path(account) + '/' + encodeURIComponent(restriction) + '/release';
  }

  private path(account: string) {
    return `/api/admin/accounts/${encodeURIComponent(account)}/restrictions`;
  }
}
