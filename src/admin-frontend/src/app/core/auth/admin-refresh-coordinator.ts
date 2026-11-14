import { HttpBackend, HttpClient, HttpContextToken, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type RenewalOutcome = 'renewed' | 'rejected' | 'unknown';
type BrowserSession = { generation: string; state: 'ready' | 'pending' | 'replacing' | 'uncertain' | 'ended' };

export const explicitSessionRecovery = new HttpContextToken(() => false);
const lockName = 'intelibill-admin-session-renewal';
const storageKey = 'intelibill.admin.session-coordination.v1';
const contextPath = '/api/admin/auth/sign-in/context';
const refreshPath = '/api/admin/auth/refresh';
const signInPath = '/api/admin/auth/sign-in';
const sessionPath = '/api/admin/auth/session';

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

  retry<T>(generation: string | undefined, request: () => Promise<T>): Promise<T> {
    return this.exclusive(() => {
      const current = this.read();
      if (current.generation !== generation || current.state === 'ended') throw new HttpErrorResponse({ status: 401 });
      return request();
    }, 'shared');
  }

  signIn(request: { email: string; password: string; otp: string }, requestToken: string): Promise<unknown> {
    return this.exclusive(async () => {
      const previous = this.read();
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

  observeSessionChange(listener: (reason: 'ended' | 'uncertain') => void): () => void {
    const onStorage = (event: StorageEvent) => {
      if (event.key !== storageKey) return;
      try {
        const previous = this.decode(event.oldValue);
        const current = this.decode(event.newValue);
        // An older event may arrive after this tab has already confirmed a newer state.
        if (event.newValue !== globalThis.localStorage.getItem(storageKey) || current.state === 'pending') return;
        if (current.state === 'uncertain' || current.state === 'replacing') listener('uncertain');
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
    if (current.generation !== generation || current.state === 'ended') return 'rejected';
    if (current.state !== 'ready') {
      try {
        // A lost refresh response may still have installed its cookies. A confirmed access cookie
        // can be used, while automatic rotation stays paused until a confirmed cookie write.
        await firstValueFrom(this.http.get(sessionPath));
        return 'renewed';
      } catch (error) {
        if (!explicit || !hasStatus(error, 401)) return 'unknown';
      }
    }
    return this.rotate(current);
  }

  private async rotate(current: BrowserSession): Promise<RenewalOutcome> {
    const { requestToken } = await firstValueFrom(this.http.get<{ requestToken: string }>(contextPath));
    // Record intent before POST so a tab closing or losing its response leaves a durable pause.
    this.write({ ...current, state: 'pending' });
    try {
      await firstValueFrom(this.http.post(refreshPath, null, { headers: { 'X-Admin-CSRF': requestToken } }));
      this.write({ ...current, state: 'ready' });
      return 'renewed';
    } catch (error) {
      const rejected = hasStatus(error, 401);
      this.write({ ...current, state: rejected ? 'ended' : 'uncertain' });
      return rejected ? 'rejected' : 'unknown';
    }
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
      || !['ready', 'pending', 'replacing', 'uncertain', 'ended'].includes(record.state)) {
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
