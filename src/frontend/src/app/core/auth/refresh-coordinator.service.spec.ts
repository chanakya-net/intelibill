import '@angular/compiler';

import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Subject, throwError } from 'rxjs';

import { FakeLockManager } from './auth.service.test-utils';
import {
  REFRESH_LEASE_POLL_MS,
  REFRESH_LEASE_STORAGE,
  REFRESH_LEASE_TTL_MS,
  REFRESH_LEASE_WAIT_LIMIT_MS,
  REFRESH_LOCK_MANAGER,
  RefreshCoordinator,
  RefreshLockManager,
} from './refresh-coordinator.service';

class MemoryStorage implements Storage {
  private readonly items = new Map<string, string>();

  get length(): number {
    return this.items.size;
  }

  clear(): void {
    this.items.clear();
  }

  getItem(key: string): string | null {
    return this.items.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.items.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.items.delete(key);
  }

  setItem(key: string, value: string): void {
    this.items.set(key, value);
  }
}

const SESSION = { userId: 'user-1', sessionId: 'session-1' } as const;
const LEASE_KEY = 'intelibill.auth.refresh-lease:user-1:session-1';

/** Two tabs: separate coordinators, one browser lock manager and one localStorage. */
function openTabs(locks: RefreshLockManager | null, storage: Storage | null): [RefreshCoordinator, RefreshCoordinator] {
  TestBed.configureTestingModule({
    providers: [
      { provide: REFRESH_LOCK_MANAGER, useValue: locks },
      { provide: REFRESH_LEASE_STORAGE, useValue: storage },
    ],
  });
  const browser = TestBed.inject(EnvironmentInjector);
  const open = (): RefreshCoordinator =>
    createEnvironmentInjector([RefreshCoordinator], browser).get(RefreshCoordinator);
  return [open(), open()];
}

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
}

describe('RefreshCoordinator', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  describe('with the Web Locks API', () => {
    it('serializesWorkForOneSession: the second tab starts only after the first finishes', async () => {
      const [tabA, tabB] = openTabs(new FakeLockManager(), null);
      const first = new Subject<string>();
      const startedB = vi.fn(() => new Subject<string>());

      tabA.runExclusive(SESSION, () => first).subscribe();
      tabB.runExclusive(SESSION, startedB).subscribe();
      await settle();
      expect(startedB).not.toHaveBeenCalled();

      first.next('renewed');
      first.complete();
      await settle();
      expect(startedB).toHaveBeenCalledTimes(1);
    });

    it('doesNotSerializeDifferentSessions: another device session renews independently', async () => {
      const [tabA, tabB] = openTabs(new FakeLockManager(), null);
      const startedB = vi.fn(() => new Subject<string>());

      tabA.runExclusive(SESSION, () => new Subject<string>()).subscribe();
      tabB.runExclusive({ userId: 'user-1', sessionId: 'session-2' }, startedB).subscribe();
      await settle();

      expect(startedB).toHaveBeenCalledTimes(1);
    });

    it('releasesTheLockWhenWorkFails: a refused renewal does not strand the other tabs', async () => {
      const [tabA, tabB] = openTabs(new FakeLockManager(), null);
      const failure = vi.fn();
      const startedB = vi.fn(() => new Subject<string>());

      tabA.runExclusive(SESSION, () => throwError(() => new Error('401'))).subscribe({ error: failure });
      tabB.runExclusive(SESSION, startedB).subscribe();
      await settle();

      expect(failure).toHaveBeenCalledTimes(1);
      expect(startedB).toHaveBeenCalledTimes(1);
    });

    it('releasesTheLockWhenUnsubscribed', async () => {
      const locks = new FakeLockManager();
      const [tabA, tabB] = openTabs(locks, null);
      const startedB = vi.fn(() => new Subject<string>());

      const held = tabA.runExclusive(SESSION, () => new Subject<string>()).subscribe();
      tabB.runExclusive(SESSION, startedB).subscribe();
      held.unsubscribe();
      await settle();

      expect(startedB).toHaveBeenCalledTimes(1);
    });

    it('holds the lock for exactly as long as the work runs, passing its values through', async () => {
      const locks = new FakeLockManager();
      const [tab] = openTabs(locks, null);
      const source = new Subject<string>();
      const values: string[] = [];
      let completed = false;

      tab.runExclusive(SESSION, () => source).subscribe({
        next: (value) => values.push(value),
        complete: () => (completed = true),
      });
      const name = locks.requested[0];
      expect(name).toBe('intelibill.auth.refresh:user-1:session-1');
      expect(locks.isHeld(name)).toBe(true);

      source.next('renewed');
      source.complete();
      await settle();

      expect(values).toEqual(['renewed']);
      expect(completed).toBe(true);
      expect(locks.isHeld(name)).toBe(false);
    });
  });

  describe('without the Web Locks API (localStorage lease)', () => {
    it('fallbackLease_SerializesWithoutWebLocks: the waiting tab starts once the lease is returned', async () => {
      vi.useFakeTimers();
      const storage = new MemoryStorage();
      const [tabA, tabB] = openTabs(null, storage);
      const first = new Subject<string>();
      const startedB = vi.fn(() => new Subject<string>());

      tabA.runExclusive(SESSION, () => first).subscribe();
      expect(storage.getItem(LEASE_KEY)).not.toBeNull();

      tabB.runExclusive(SESSION, startedB).subscribe();
      await vi.advanceTimersByTimeAsync(REFRESH_LEASE_POLL_MS * 3);
      expect(startedB).not.toHaveBeenCalled();

      first.complete();
      await vi.advanceTimersByTimeAsync(REFRESH_LEASE_POLL_MS);
      expect(startedB).toHaveBeenCalledTimes(1);
    });

    it('fallbackLease_GivesUpAfterTheBound: a stuck lease delays a renewal, never blocks it', async () => {
      vi.useFakeTimers();
      const storage = new MemoryStorage();
      storage.setItem(LEASE_KEY, JSON.stringify({ owner: 'tab-that-hung', expiresAt: Date.now() + REFRESH_LEASE_TTL_MS * 10 }));
      const [tab] = openTabs(null, storage);
      const started = vi.fn(() => new Subject<string>());

      tab.runExclusive(SESSION, started).subscribe();
      await vi.advanceTimersByTimeAsync(REFRESH_LEASE_WAIT_LIMIT_MS - REFRESH_LEASE_POLL_MS);
      expect(started).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(REFRESH_LEASE_POLL_MS * 2);
      expect(started).toHaveBeenCalledTimes(1);
    });

    it('takes over a lease whose holder has expired', () => {
      const storage = new MemoryStorage();
      storage.setItem(LEASE_KEY, JSON.stringify({ owner: 'closed-tab', expiresAt: Date.now() - 1 }));
      const [tab] = openTabs(null, storage);
      const started = vi.fn(() => new Subject<string>());

      tab.runExclusive(SESSION, started).subscribe();

      expect(started).toHaveBeenCalledTimes(1);
    });

    it('returns the lease when the work finishes', async () => {
      const storage = new MemoryStorage();
      const [tab] = openTabs(null, storage);
      const work = new Subject<string>();

      tab.runExclusive(SESSION, () => work).subscribe();
      expect(storage.getItem(LEASE_KEY)).not.toBeNull();

      work.complete();
      await settle();
      expect(storage.getItem(LEASE_KEY)).toBeNull();
    });

    it('still renews when there is no storage to coordinate through', () => {
      const [tab] = openTabs(null, null);
      const started = vi.fn(() => new Subject<string>());

      tab.runExclusive(SESSION, started).subscribe();

      expect(started).toHaveBeenCalledTimes(1);
    });
  });
});
