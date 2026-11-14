import { vi } from 'vitest';

export function stubAdminBrowserCoordination() {
  const entries = new Map<string, string>();
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => entries.get(key) ?? null,
    setItem: (key: string, value: string) => entries.set(key, value),
    removeItem: (key: string) => entries.delete(key),
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

export function stubQueuedAdminBrowserLock() {
  let tail = Promise.resolve();
  Object.defineProperty(navigator, 'locks', {
    configurable: true,
    value: {
      request: (_name: string, _options: unknown, task: () => Promise<unknown>) => {
        const result = tail.then(task);
        tail = result.then(() => undefined, () => undefined);
        return result;
      },
    },
  });
}
