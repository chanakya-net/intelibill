import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NetworkStatusService } from './network-status.service';

describe('NetworkStatusService', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  function makeService(): NetworkStatusService {
    TestBed.configureTestingModule({ providers: [NetworkStatusService] });
    return TestBed.inject(NetworkStatusService);
  }

  it('initial browser online state reflects navigator.onLine', () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(new Response('{}', { status: 200 }));

    const service = makeService();

    expect(service.isOnline()).toBe(true);
  });

  it('browser offline event sets isOnline false and canReachApi false immediately', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(new Response('{}', { status: 200 }));

    const service = makeService();
    expect(service.isOnline()).toBe(true);

    window.dispatchEvent(new Event('offline'));
    await vi.runAllTimersAsync();

    expect(service.isOnline()).toBe(false);
    expect(service.canReachApi()).toBe(false);
  });

  it('successful ping sets canReachApi true and updates lastVerifiedAt', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(new Response(JSON.stringify({ serverTime: new Date().toISOString() }), { status: 200 }));

    const service = makeService();
    const before = Date.now();
    await service.checkConnectivity();

    expect(service.canReachApi()).toBe(true);
    expect(service.lastVerifiedAt()).not.toBeNull();
    expect(service.lastVerifiedAt()!.getTime()).toBeGreaterThanOrEqual(before);
  });

  it('failed ping sets canReachApi false and does not update lastVerifiedAt', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockRejectedValue(new TypeError('network error'));

    const service = makeService();
    const checkPromise = service.checkConnectivity();
    await vi.runAllTimersAsync();
    await checkPromise;

    expect(service.canReachApi()).toBe(false);
    expect(service.lastVerifiedAt()).toBeNull();
  });

  it('non-ok HTTP response sets canReachApi false', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(new Response(null, { status: 503 }));

    const service = makeService();
    const checkPromise = service.checkConnectivity();
    await vi.runAllTimersAsync();
    await checkPromise;

    expect(service.canReachApi()).toBe(false);
  });

  it('retries on transient failure and succeeds on later attempt', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy
      .mockRejectedValueOnce(new TypeError('network error'))
      .mockResolvedValue(new Response('{}', { status: 200 }));

    const service = makeService();
    const checkPromise = service.checkConnectivity();
    await vi.runAllTimersAsync();
    await checkPromise;

    expect(service.canReachApi()).toBe(true);
    expect(service.lastVerifiedAt()).not.toBeNull();
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });

  it('exhausts all retries and sets canReachApi false', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockRejectedValue(new TypeError('network error'));

    const service = makeService();
    const checkPromise = service.checkConnectivity();
    await vi.runAllTimersAsync();
    await checkPromise;

    expect(service.canReachApi()).toBe(false);
    expect(fetchSpy).toHaveBeenCalledTimes(3);
  });

  it('isChecking is true during a ping and false after', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    let resolveFetch!: (r: Response) => void;
    fetchSpy.mockImplementation(
      () => new Promise<Response>((res) => { resolveFetch = res; }),
    );

    const service = makeService();
    const checkPromise = service.checkConnectivity();

    expect(service.isChecking()).toBe(true);

    resolveFetch(new Response('{}', { status: 200 }));
    await checkPromise;

    expect(service.isChecking()).toBe(false);
  });

  it('ping fetch uses cache: no-store to bypass service worker cache', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(new Response('{}', { status: 200 }));

    const service = makeService();
    await service.checkConnectivity();

    expect(fetchSpy).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ cache: 'no-store' }),
    );
  });

  it('online event after offline triggers a connectivity check', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
    fetchSpy.mockResolvedValue(new Response('{}', { status: 200 }));

    const service = makeService();
    expect(service.isOnline()).toBe(false);

    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    window.dispatchEvent(new Event('online'));
    await vi.runAllTimersAsync();

    expect(service.isOnline()).toBe(true);
    expect(fetchSpy).toHaveBeenCalled();
  });
});
