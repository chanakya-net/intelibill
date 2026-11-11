import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { OfflinePackStatus, OfflinePackStatusStorage } from './offline-pack-status.storage';

const KEY = 'intelibill.offlineSales.offlinePack.v1';

const STATUS: OfflinePackStatus = {
  state: 'ready',
  buildId: '0123456789abcdef',
  manifestHash: 'abc',
  persisted: false,
  lastCheckedAt: '2026-11-10T10:00:00.000Z',
  lastUpdatedAt: '2026-11-09T10:00:00.000Z',
};

describe('OfflinePackStatusStorage', () => {
  const storage = new OfflinePackStatusStorage();
  let original: PropertyDescriptor | undefined;

  beforeEach(() => {
    original = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
    const store = new Map<string, string>();
    Object.defineProperty(globalThis, 'localStorage', {
      configurable: true,
      value: {
        get length() {
          return store.size;
        },
        clear: () => store.clear(),
        getItem: (key: string) => store.get(key) ?? null,
        key: (index: number) => [...store.keys()][index] ?? null,
        removeItem: (key: string) => void store.delete(key),
        setItem: (key: string, value: string) => void store.set(key, value),
      } satisfies Storage,
    });
  });

  afterEach(() => {
    if (original) Object.defineProperty(globalThis, 'localStorage', original);
    else delete (globalThis as { localStorage?: Storage }).localStorage;
  });

  it('keeps the device-level pack status and forgets it on null', () => {
    expect(storage.loadPackStatus()).toBeNull();

    storage.savePackStatus(STATUS);
    expect(storage.loadPackStatus()).toEqual(STATUS);

    storage.savePackStatus(null);
    expect(localStorage.getItem(KEY)).toBeNull();
  });

  it('ignores a damaged or unknown record', () => {
    localStorage.setItem(KEY, '{nope');
    expect(storage.loadPackStatus()).toBeNull();

    localStorage.setItem(KEY, JSON.stringify({ ...STATUS, state: 'maybe' }));
    expect(storage.loadPackStatus()).toBeNull();

    localStorage.setItem(KEY, JSON.stringify({ state: 'incomplete', buildId: 5 }));
    expect(storage.loadPackStatus()).toEqual({
      state: 'incomplete',
      buildId: null,
      manifestHash: null,
      persisted: null,
      lastCheckedAt: null,
      lastUpdatedAt: null,
    });
  });
});
