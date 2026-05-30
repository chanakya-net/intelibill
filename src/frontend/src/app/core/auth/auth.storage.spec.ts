import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthStorage } from './auth.storage';

describe('AuthStorage remembered identifier', () => {
  const LAST_IDENTIFIER_KEY = 'inventory.auth.last-identifier';
  const LAST_EMAIL_KEY = 'inventory.auth.last-email';

  const originalLocalStorage = globalThis.localStorage;
  let storage: AuthStorage;
  let backingStore: Map<string, string>;

  beforeEach(() => {
    backingStore = new Map<string, string>();
    const mockLocalStorage: Storage = {
      length: 0,
      clear: () => {
        backingStore.clear();
      },
      getItem: (key: string) => backingStore.get(key) ?? null,
      key: (index: number) => Array.from(backingStore.keys())[index] ?? null,
      removeItem: (key: string) => {
        backingStore.delete(key);
      },
      setItem: (key: string, value: string) => {
        backingStore.set(key, value);
      },
    };
    vi.stubGlobal('localStorage', mockLocalStorage);
    storage = new AuthStorage();
  });

  afterEach(() => {
    if (originalLocalStorage) {
      vi.stubGlobal('localStorage', originalLocalStorage);
    } else {
      vi.unstubAllGlobals();
    }
  });

  it('writes remembered identifier to the new key', () => {
    storage.saveLastIdentifier('user@example.com');

    expect(localStorage.getItem(LAST_IDENTIFIER_KEY)).toBe('user@example.com');
  });

  it('reads remembered identifier from new key first', () => {
    localStorage.setItem(LAST_EMAIL_KEY, 'legacy@example.com');
    localStorage.setItem(LAST_IDENTIFIER_KEY, 'current@example.com');

    expect(storage.getLastIdentifier()).toBe('current@example.com');
  });

  it('falls back to legacy last-email key when new key is missing', () => {
    localStorage.setItem(LAST_EMAIL_KEY, 'legacy@example.com');

    expect(storage.getLastIdentifier()).toBe('legacy@example.com');
  });

  it('returns empty string when neither identifier key exists', () => {
    expect(storage.getLastIdentifier()).toBe('');
  });

  it('clears both new and legacy remembered keys', () => {
    localStorage.setItem(LAST_IDENTIFIER_KEY, 'current@example.com');
    localStorage.setItem(LAST_EMAIL_KEY, 'legacy@example.com');

    storage.clearLastIdentifier();

    expect(localStorage.getItem(LAST_IDENTIFIER_KEY)).toBeNull();
    expect(localStorage.getItem(LAST_EMAIL_KEY)).toBeNull();
  });
});
