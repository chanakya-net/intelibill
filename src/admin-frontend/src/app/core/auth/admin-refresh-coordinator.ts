import { HttpBackend, HttpClient, HttpContextToken, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type RenewalOutcome = 'renewed' | 'rejected' | 'unknown';
type BrowserSession = { generation: string; state: 'ready' | 'pending' | 'replacing' | 'uncertain' | 'ended' | 'signed-out' };
type SessionChange = 'ended' | 'uncertain' | 'sign-out';
type SignOutOutcome = 'confirmed' | 'unconfirmed' | 'superseded';

export const explicitSessionRecovery = new HttpContextToken(() => false);
const lockName = 'intelibill-admin-session-renewal';
const storageKey = 'intelibill.admin.session-coordination.v1';
const endIntentPrefix = storageKey + '.sign-out.';
const contextPath = '/api/admin/auth/sign-in/context';
const refreshPath = '/api/admin/auth/refresh';
const signInPath = '/api/admin/auth/sign-in';
const sessionPath = '/api/admin/auth/session';
const signOutPath = '/api/admin/auth/sign-out';

/** Serializes writes to the HttpOnly session cookies across tabs. Storage holds only coordination metadata. */
@Injectable({ providedIn: 'root' })
export class AdminRefreshCoordinator {
  private readonly http = new HttpClient(inject(HttpBackend));
  private pending?: { generation: string; result: Promise<RenewalOutcome> };

  // A null generation means a sign-in is changing the cookie pair.
  generation(): string | null | undefined {
    try {
      const current = this.read();
      return current.state === 'replacing' ? null : current.generation;
    } catch {
      return undefined;
    }
  }

  acceptsResponse(generation: string | undefined): boolean {
    try {
      const current = this.read();
      return current.generation === generation && current.state !== 'ended' && current.state !== 'replacing' && !this.blocksForSignOut(current);
    } catch {
      return generation === undefined;
    }
  }

  async renew(generation: string | undefined, explicit: boolean): Promise<RenewalOutcome> {
    if (generation === undefined) return 'unknown';
    if (!explicit && this.pending?.generation === generation) return this.pending.result;
    const result = this.exclusive(() => this.resume(generation, explicit)).catch(() => 'unknown' as const);
    if (explicit) return result;
    this.pending = { generation, result };
    try {
      return await result;
    } finally {
      if (this.pending?.result === result) this.pending = undefined;
    }
  }

  /**
   * Explicit recovery from a sign-in that never settled (its tab closed with the POST in flight).
   * The exclusive lock waits for any live sign-in; a marker still present afterwards is abandoned
   * and becomes unconfirmed, so the caller checks the session before any rotation.
   */
  recoverAbandonedSignIn(): Promise<string | undefined> {
    return this.exclusive(async () => {
      const current = this.read();
      if (current.state === 'replacing') this.write({ ...current, state: 'uncertain' });
      return current.generation;
    }).catch(() => undefined);
  }

  retry<T>(generation: string | undefined, request: () => Promise<T>): Promise<T> {
    return this.exclusive(() => {
      const current = this.read();
      if (current.generation !== generation || current.state === 'ended' || this.blocksForSignOut(current)) throw new HttpErrorResponse({ status: 401 });
      return request();
    }, 'shared');
  }

  signIn(request: { email: string; password: string; otp: string }, requestToken: string): Promise<unknown> {
    return this.exclusive(async () => {
      let previous = this.read();
      if (previous.state !== 'signed-out' && this.blocksForSignOut(previous)) {
        await this.finishSignOut(previous.generation);
        previous = this.read();
      }
      const replacement: BrowserSession = { generation: crypto.randomUUID(), state: 'replacing' };
      this.write(replacement);
      try {
        const answer = await firstValueFrom(this.http.post(signInPath, request, {
          headers: { 'X-Admin-CSRF': requestToken },
        }));
        this.write({ ...replacement, state: 'ready' });
        return answer;
      } catch (error) {
        this.write(definitiveSignInFailure(error) ? previous : { ...replacement, state: 'uncertain' });
        throw error;
      }
    });
  }

  signOutMarker(): { generation: string; confirmed: boolean } | null {
    try {
      const current = this.read();
      return this.blocksForSignOut(current)
        ? { generation: current.generation, confirmed: current.state === 'signed-out' } : null;
    } catch {
      return null;
    }
  }

  async signOut(generation: string | undefined): Promise<SignOutOutcome> {
    try {
      if (generation === undefined) return 'unconfirmed';
      const current = this.read();
      if (current.generation !== generation || current.state === 'replacing') return 'superseded';
      if (current.state === 'signed-out') return 'confirmed';

      // Use a separate generation-bound key so a refresh settling in another tab cannot erase intent.
      globalThis.localStorage.setItem(endIntentPrefix + generation, '1');
      return await this.exclusive(() => this.finishSignOut(generation));
    } catch {
      return 'unconfirmed';
    }
  }

  private async finishSignOut(generation: string): Promise<SignOutOutcome> {
    const current = this.read();
    if (current.generation !== generation || !this.blocksForSignOut(current)) return 'superseded';
    if (current.state === 'signed-out') return 'confirmed';
    const { requestToken } = await firstValueFrom(this.http.get<{ requestToken: string }>(contextPath));
    await firstValueFrom(this.http.post(signOutPath, null, { headers: { 'X-Admin-CSRF': requestToken } }));
    this.write({ generation, state: 'signed-out' });
    globalThis.localStorage.removeItem(endIntentPrefix + generation);
    return 'confirmed';
  }

  private blocksForSignOut(current: BrowserSession): boolean {
    return current.state === 'signed-out' || globalThis.localStorage.getItem(endIntentPrefix + current.generation) === '1';
  }

  observeSessionChange(listener: (reason: SessionChange) => void): () => void {
    const onStorage = (event: StorageEvent) => {
      if (event.key?.startsWith(endIntentPrefix)) {
        if (this.signOutMarker()) listener('sign-out');
        return;
      }
      if (event.key !== storageKey) return;
      try {
        const previous = this.decode(event.oldValue);
        const current = this.decode(event.newValue);
        // An older event may arrive after this tab has already confirmed a newer state.
        if (event.newValue !== globalThis.localStorage.getItem(storageKey) || current.state === 'pending') return;
        if (this.blocksForSignOut(current)) listener('sign-out');
        else if (current.state === 'uncertain' || current.state === 'replacing') listener('uncertain');
        else if (current.state === 'ended' || previous.state === 'replacing'
          || current.generation !== previous.generation) listener('ended');
      } catch {
        listener('uncertain');
      }
    };
    globalThis.addEventListener('storage', onStorage);
    return () => globalThis.removeEventListener('storage', onStorage);
  }

  private async resume(generation: string, explicit: boolean): Promise<RenewalOutcome> {
    const current = this.read();
    if (current.generation !== generation || current.state === 'ended' || this.blocksForSignOut(current)) return 'rejected';
    if (current.state !== 'ready') {
      try {
        // A lost refresh response may still have installed its cookies. A confirmed access cookie
        // can be used, while automatic rotation stays paused until a confirmed cookie write.
        await firstValueFrom(this.http.get(sessionPath));
        return this.acceptsResponse(generation) ? 'renewed' : 'rejected';
      } catch (error) {
        if (!explicit || !hasStatus(error, 401)) return 'unknown';
      }
    }
    return this.rotate(current);
  }

  private async rotate(current: BrowserSession): Promise<RenewalOutcome> {
    const { requestToken } = await firstValueFrom(this.http.get<{ requestToken: string }>(contextPath));
    // Sign-out can publish its intent while a refresh waits on CSRF or its response.
    const latest = this.read();
    if (latest.generation !== current.generation || latest.state !== current.state || this.blocksForSignOut(latest)) return 'rejected';
    this.write({ ...current, state: 'pending' });
    try {
      await firstValueFrom(this.http.post(refreshPath, null, { headers: { 'X-Admin-CSRF': requestToken } }));
      return this.completeRotation(current, 'ready') ? 'renewed' : 'rejected';
    } catch (error) {
      const rejected = hasStatus(error, 401);
      if (!this.completeRotation(current, rejected ? 'ended' : 'uncertain')) return 'rejected';
      return rejected ? 'rejected' : 'unknown';
    }
  }

  private completeRotation(original: BrowserSession, state: BrowserSession['state']): boolean {
    const current = this.read();
    if (current.generation !== original.generation || current.state !== 'pending' || this.blocksForSignOut(current)) return false;
    this.write({ ...original, state });
    return true;
  }

  private async exclusive<T>(task: () => Promise<T>, mode: LockMode = 'exclusive'): Promise<T> {
    const locks = globalThis.navigator?.locks;
    if (!locks) throw new Error('Provider session coordination unavailable');
    return locks.request(lockName, { mode }, task);
  }

  private read(): BrowserSession {
    return this.decode(globalThis.localStorage.getItem(storageKey));
  }

  private write(session: BrowserSession) {
    globalThis.localStorage.setItem(storageKey, JSON.stringify(session));
  }

  private decode(value: string | null): BrowserSession {
    if (value === null) return { generation: '', state: 'ready' };
    const record = JSON.parse(value) as Partial<BrowserSession> | null;
    if (typeof record?.generation !== 'string'
      || !record.state
      || !['ready', 'pending', 'replacing', 'uncertain', 'ended', 'signed-out'].includes(record.state)) {
      throw new Error('Provider session coordination state unavailable');
    }
    return { generation: record.generation, state: record.state };
  }
}

function hasStatus(error: unknown, status: number): boolean {
  return error instanceof HttpErrorResponse && error.status === status;
}

function definitiveSignInFailure(error: unknown): boolean {
  return error instanceof HttpErrorResponse && [400, 401, 403, 413, 429].includes(error.status);
}
