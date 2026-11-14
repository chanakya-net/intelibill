import { HttpBackend, HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** `unknown`: the server may or may not have rotated the credential; never retried on its own. */
export type RenewalOutcome = 'renewed' | 'rejected' | 'unknown';

const lockName = 'intelibill-admin-session-renewal';
const contextPath = '/api/admin/auth/sign-in/context';
const refreshPath = '/api/admin/auth/refresh';

/**
 * Renews the provider session from its HttpOnly refresh cookie. A refresh credential is single-use
 * and a second presentation ends the session, so renewals never overlap: requests in this tab share
 * one renewal, and tabs take turns under a Web Lock. A turn starts after the previous tab stored its
 * rotated cookie, so each renewal presents the current credential.
 */
@Injectable({ providedIn: 'root' })
export class AdminRefreshCoordinator {
  // Straight to the backend: renewal requests never pass back through the session interceptor.
  private readonly http = new HttpClient(inject(HttpBackend));
  private pending?: Promise<RenewalOutcome>;

  renew(): Promise<RenewalOutcome> {
    this.pending ??= this.exclusive(() => this.rotate()).finally(() => (this.pending = undefined));
    return this.pending;
  }

  private async exclusive(task: () => Promise<RenewalOutcome>): Promise<RenewalOutcome> {
    const locks = globalThis.navigator?.locks;
    return locks ? await locks.request(lockName, { mode: 'exclusive' }, task) : await task();
  }

  private async rotate(): Promise<RenewalOutcome> {
    try {
      const { requestToken } = await firstValueFrom(this.http.get<{ requestToken: string }>(contextPath));
      await firstValueFrom(this.http.post(refreshPath, null, { headers: { 'X-Admin-CSRF': requestToken } }));
      return 'renewed';
    } catch (error) {
      return error instanceof HttpErrorResponse && error.status === 401 ? 'rejected' : 'unknown';
    }
  }
}
