import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';

export interface TrialExtensionDraft {
  readonly endsAt: string;
  readonly reason: string;
  readonly reviewReference: string;
}

export interface TrialExtensionCommand extends TrialExtensionDraft {
  readonly operationKey: string;
  readonly currentVersion: number;
  readonly sourceHash: string;
  readonly confirmed: true;
}

interface TrialExtensionFacts extends TrialExtensionDraft {
  readonly shopId: string;
  readonly shopName: string;
  readonly startsAt: string;
  readonly originalEndsAt: string;
  readonly accountTrialCap: number;
  readonly accountTrialCapReached: boolean;
}

export interface TrialExtensionPreview extends TrialExtensionFacts {
  readonly currentEndsAt: string;
  readonly currentVersion: number;
  readonly sourceHash: string;
}

export interface TrialExtensionResult extends TrialExtensionFacts {
  readonly operationKey: string;
  readonly previousEndsAt: string;
  readonly extendedVersion: number;
}

@Injectable({ providedIn: 'root' })
export class TrialExtensionApi {
  private readonly http = inject(HttpClient);

  preview(account: string, shop: string, draft: TrialExtensionDraft, sessionId: string) {
    return this.post<TrialExtensionPreview>(this.path(account, shop) + '/preview', draft, sessionId);
  }

  confirm(account: string, shop: string, command: TrialExtensionCommand, sessionId: string) {
    return this.post<TrialExtensionResult>(this.path(account, shop), command, sessionId);
  }

  recover(account: string, shop: string, key: string, sessionId: string) {
    return this.http.get<TrialExtensionResult>(this.path(account, shop) + '/operations/' + encodeURIComponent(key),
      { headers: { 'X-Admin-Session': sessionId } });
  }

  private post<T>(url: string, body: TrialExtensionDraft, sessionId: string) {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.post<T>(url, body,
        { headers: { 'X-Admin-CSRF': requestToken, 'X-Admin-Session': sessionId } })),
    );
  }

  private path(account: string, shop: string) {
    return `/api/admin/accounts/${encodeURIComponent(account)}/shops/${encodeURIComponent(shop)}/trial-extensions`;
  }
}

/** Keep the optional fraction as text so entering microseconds never rounds to milliseconds. */
export function exactIstInstant(value: string): string | null {
  const match = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2})(:\d{2}(?:\.\d{1,6})?)?$/.exec(value.trim());
  return match ? `${match[1]}T${match[2]}${match[3] ?? ':00'}+05:30` : null;
}
