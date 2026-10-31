import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { map, Observable } from 'rxjs';

import { DeviceSession } from '../../../core/auth/auth.models';
import { SESSION_ENDPOINTS } from '../../../core/auth/auth.constants';

/**
 * The caller's own signed-in devices. Every call is scoped to the account the request authenticates
 * as; there is no way to reach another account's devices, and a session id that is not yours simply
 * is not found.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);

  getMySessions(): Observable<readonly DeviceSession[]> {
    return this.http.get<readonly DeviceSession[]>(SESSION_ENDPOINTS.list);
  }

  /** Ends one device. Ending the device you are on signs this browser out. */
  revokeSession(sessionId: string): Observable<void> {
    return this.http.delete(SESSION_ENDPOINTS.revoke(sessionId)).pipe(map(() => void 0));
  }

  /** Ends every device, this one included. */
  revokeAllSessions(): Observable<void> {
    return this.http.post(SESSION_ENDPOINTS.revokeAll, {}).pipe(map(() => void 0));
  }
}
