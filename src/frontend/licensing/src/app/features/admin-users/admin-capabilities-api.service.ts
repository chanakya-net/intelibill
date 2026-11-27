import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';

export const manageAdminAccess = 'admin.access.manage';
export const adminGrants = {
  support: 'Support',
  billing: 'Billing',
  'licensing-manager': 'Licensing Manager',
  'security-administrator': 'Security Administrator',
  'platform-owner': 'Platform Owner',
} as const;

export type AdminGrant = keyof typeof adminGrants;
export type AdminAccessReason = 'responsibilities-changed' | 'access-review' | 'access-withdrawn';

export interface AdministratorAccess {
  id: string;
  email: string;
  version: number;
  capabilities: readonly AdminGrant[];
  mfaEnrolled: boolean;
  disabled: boolean;
  isCurrent: boolean;
}

interface AccessChange {
  expectedVersion: number;
  capabilities: readonly AdminGrant[];
  reason: AdminAccessReason;
  confirmed: boolean;
  disable: boolean;
}

interface AccessPage {
  items: readonly AdministratorAccess[];
  nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminCapabilitiesApi {
  private readonly http = inject(HttpClient);
  private readonly path = '/api/admin/users';

  list(cursor: string | null) {
    return this.http.get<AccessPage>(this.path, { params: cursor ? { cursor } : {} });
  }

  update(id: string, command: AccessChange) {
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => this.http.put<AdministratorAccess>(
        this.path + '/' + id + '/capabilities', command, { headers: { 'X-Admin-CSRF': requestToken } },
      )),
    );
  }
}
