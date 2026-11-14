import { vi } from 'vitest';

export function stubAdminBrowserCoordination() {
  const entries = new Map<string, string>();
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => entries.get(key) ?? null,
    setItem: (key: string, value: string) => entries.set(key, value),
  });
  Object.defineProperty(navigator, 'locks', {
    configurable: true,
    value: { request: async (_name: string, _options: unknown, task: () => Promise<unknown>) => task() },
  });
}

export function restoreAdminBrowserCoordination() {
  vi.unstubAllGlobals();
  Reflect.deleteProperty(navigator, 'locks');
}
