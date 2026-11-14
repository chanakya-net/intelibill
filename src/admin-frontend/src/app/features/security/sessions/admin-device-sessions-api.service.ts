import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap, throwError } from 'rxjs';
import { AdminRefreshCoordinator } from '../../../core/auth/admin-refresh-coordinator';
import { AdminSessionChangedError } from '../../auth/admin-session.service';

export interface AdminDeviceSession {
  id: string;
  isCurrent: boolean;
  createdAt: string;
  lastSeenAt: string;
  absoluteExpiresAt: string;
}

export interface AdminDevicePage {
  items: readonly AdminDeviceSession[];
  nextCursor: string | null;
}

export type RevokeReason = 'device-unused' | 'device-unrecognized';
const sessionsPath = '/api/admin/sessions';

@Injectable({ providedIn: 'root' })
export class AdminDeviceSessionsApi {
  private readonly http = inject(HttpClient);
  private readonly coordinator = inject(AdminRefreshCoordinator);

  list(cursor: string | null) {
    const params = cursor ? new HttpParams().set('cursor', cursor) : undefined;
    return this.http.get<AdminDevicePage>(sessionsPath, { params });
  }

  revoke(id: string, reason: RevokeReason) {
    const generation = this.coordinator.generation();
    const path = sessionsPath + '/' + encodeURIComponent(id) + '/revoke';
    return this.http.get<{ requestToken: string }>('/api/admin/auth/sign-in/context').pipe(
      switchMap(({ requestToken }) => generation !== null && this.coordinator.acceptsResponse(generation)
        ? this.http.post<void>(path, { reason }, { headers: { 'X-Admin-CSRF': requestToken } })
        : throwError(() => new AdminSessionChangedError(path))),
    );
  }
}
