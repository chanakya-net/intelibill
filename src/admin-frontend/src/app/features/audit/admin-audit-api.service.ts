import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export const identityAuditCapability = 'admin.audit.identity.read';

export const auditActions = {
  'proof-prepared': 'Bootstrap proof prepared',
  'proof-replaced': 'Bootstrap proof replaced',
  'owner-created': 'Owner created',
  'proof-refused': 'Bootstrap proof refused',
  'enrollment-resumed': 'Enrollment resumed',
  'resume-refused': 'Enrollment resume refused',
  'invitation-issued': 'Invitation issued',
  'invitation-accepted': 'Invitation accepted',
  'invitation-refused': 'Invitation refused',
  'invitation-superseded': 'Invitation superseded',
  'invitation-undelivered': 'Invitation undelivered',
  'mfa-enrolled': 'Authenticator enrolled',
  'mfa-enrollment-refused': 'Authenticator enrollment refused',
  'sign-in-succeeded': 'Sign-in succeeded',
  'sign-in-refused': 'Sign-in refused',
  'session-reuse-revoked': 'Session revoked for credential reuse',
  'device-sessions-inspected': 'Device sessions inspected',
  'device-session-revoked': 'Device session revoked',
  'device-signed-out': 'Device signed out',
  'device-session-denied': 'Device session action denied',
  'audit-read-succeeded': 'Audit history inspected',
  'audit-read-denied': 'Audit history access denied',
  'invitation-management-denied': 'Invitation management denied',
} as const;

export interface AuditFilters {
  action?: string;
  fromUtc?: string;
  toUtc?: string;
  pageSize: number;
}

export interface AuditReference {
  targetKind: string;
  targetId: string | null;
  state: string;
  version: number | null;
  milestoneAt: string | null;
}

export interface AuditEvent {
  id: string;
  scope: 'provider-identity';
  action: keyof typeof auditActions;
  outcome: 'succeeded' | 'denied' | 'failed';
  occurredAt: string;
  actorKind: string | null;
  actorId: string | null;
  reasonCode: string | null;
  correlationId: string | null;
  before: AuditReference | null;
  after: AuditReference | null;
}

export interface AuditPage {
  items: readonly AuditEvent[];
  nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminAuditApi {
  private readonly http = inject(HttpClient);

  read(filters: AuditFilters, cursor: string | null) {
    let params = new HttpParams().set('scope', 'provider-identity').set('pageSize', filters.pageSize);
    if (filters.action) params = params.set('action', filters.action);
    if (filters.fromUtc) params = params.set('fromUtc', filters.fromUtc);
    if (filters.toUtc) params = params.set('toUtc', filters.toUtc);
    if (cursor) params = params.set('cursor', cursor);
    return this.http.get<AuditPage>('/api/admin/audit', { params });
  }
}
