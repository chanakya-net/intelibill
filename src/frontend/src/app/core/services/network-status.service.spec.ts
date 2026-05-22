import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NetworkStatusService } from './network-status.service';

describe('NetworkStatusService', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  const serverTime = '2026-05-21T12:34:56.789Z';

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

  function successfulPingResponse(time = serverTime): Response {
    return new Response(JSON.stringify({ serverTime: time }), { status: 200 });
  }

  it('initial browser online state reflects navigator.onLine', () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(successfulPingResponse());

    const service = makeService();

    expect(service.isOnline()).toBe(true);
  });

  it('browser offline event sets isOnline false and canReachApi false immediately', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(successfulPingResponse());

    const service = makeService();
    expect(service.isOnline()).toBe(true);

    window.dispatchEvent(new Event('offline'));
    await vi.runAllTimersAsync();

    expect(service.isOnline()).toBe(false);
    expect(service.canReachApi()).toBe(false);
  });

  it('successful ping sets canReachApi true and stores the API serverTime', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(successfulPingResponse());

    const service = makeService();
    await service.checkConnectivity();

    expect(service.canReachApi()).toBe(true);
    expect(service.lastVerifiedAt()?.toISOString()).toBe(serverTime);
  });

  it('ignores a successful ping that resolves after the browser goes offline', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    let resolveFetch!: (r: Response) => void;
    fetchSpy.mockImplementation(
      () => new Promise<Response>((res) => { resolveFetch = res; }),
    );

    const service = makeService();
    const checkPromise = service.checkConnectivity();

    expect(service.isChecking()).toBe(true);

    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
    window.dispatchEvent(new Event('offline'));

    expect(service.isOnline()).toBe(false);
    expect(service.canReachApi()).toBe(false);

    resolveFetch(successfulPingResponse());
    await checkPromise;

    expect(service.isOnline()).toBe(false);
    expect(service.canReachApi()).toBe(false);
    expect(service.lastVerifiedAt()).toBeNull();
  });

  it('does not ping when the browser is already offline', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
    fetchSpy.mockResolvedValue(successfulPingResponse());

    const service = makeService();
    await service.checkConnectivity();

    expect(service.isOnline()).toBe(false);
    expect(service.canReachApi()).toBe(false);
    expect(fetchSpy).not.toHaveBeenCalled();
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
      .mockResolvedValue(successfulPingResponse());

    const service = makeService();
    const checkPromise = service.checkConnectivity();
    await vi.runAllTimersAsync();
    await checkPromise;

    expect(service.canReachApi()).toBe(true);
    expect(service.lastVerifiedAt()).not.toBeNull();
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });

  it('stops retrying when the browser goes offline', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockRejectedValue(new TypeError('network error'));

    const service = makeService();
    const checkPromise = service.checkConnectivity();
    await vi.advanceTimersByTimeAsync(1);

    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
    await vi.runAllTimersAsync();
    await checkPromise;

    expect(service.isOnline()).toBe(false);
    expect(service.canReachApi()).toBe(false);
    expect(fetchSpy).toHaveBeenCalledTimes(1);
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

    resolveFetch(successfulPingResponse());
    await checkPromise;

    expect(service.isChecking()).toBe(false);
  });

  it('ping fetch uses cache: no-store to bypass service worker cache', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(successfulPingResponse());

    const service = makeService();
    await service.checkConnectivity();

    expect(fetchSpy).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ cache: 'no-store' }),
    );
  });

  it('ping fetch includes ngsw-bypass query to avoid stale service worker API responses', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    fetchSpy.mockResolvedValue(successfulPingResponse());

    const service = makeService();
    await service.checkConnectivity();

    expect(fetchSpy).toHaveBeenCalledWith(
      expect.stringContaining('ngsw-bypass=true'),
      expect.any(Object),
    );
  });

  it('online event after offline triggers a connectivity check', async () => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
    fetchSpy.mockResolvedValue(successfulPingResponse());

    const service = makeService();
    expect(service.isOnline()).toBe(false);

    Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
    window.dispatchEvent(new Event('online'));
    await vi.runAllTimersAsync();

    expect(service.isOnline()).toBe(true);
    expect(fetchSpy).toHaveBeenCalled();
  });
});
