import { signal } from '@angular/core';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { ShopUpdatePayload, ShopUpdatesSignalRService } from './shop-updates-signalr.service';

const { mockHandlers, mockConnection, capturedUrls } = vi.hoisted(() => {
  const handlers = new Map<string, (payload: unknown) => void>();
  const urls: string[] = [];
  const connection = {
    on: (event: string, handler: (payload: unknown) => void) => handlers.set(event, handler),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
  };

  return { mockHandlers: handlers, mockConnection: connection, capturedUrls: urls };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(url: string) {
      capturedUrls.push(url);
      return this;
    }

    withAutomaticReconnect() {
      return this;
    }

    build() {
      return mockConnection;
    }
  },
}));

describe('ShopUpdatesSignalRService', () => {
  const activeSession = signal({ activeShopId: 'shop-xyz', accessToken: 'test-token' });
  const authService = {
    session: activeSession,
    getAccessToken: () => activeSession()?.accessToken ?? '',
  };

  let service: ShopUpdatesSignalRService;

  function emitEvent(event: string, payload: unknown): void {
    mockHandlers.get(event)?.(payload);
  }

  beforeEach(async () => {
    mockHandlers.clear();
    capturedUrls.length = 0;
    mockConnection.start.mockReset().mockResolvedValue(undefined);
    mockConnection.stop.mockReset().mockResolvedValue(undefined);
    activeSession.set({ activeShopId: 'shop-xyz', accessToken: 'test-token' });

    TestBed.configureTestingModule({
      providers: [
        ShopUpdatesSignalRService,
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: AuthService, useValue: authService },
      ],
    });

    service = TestBed.inject(ShopUpdatesSignalRService);
    (TestBed as unknown as { flushEffects?: () => void }).flushEffects?.();
    await service.startConnection();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.clearAllMocks();
  });

  it('connects to the shop updates hub for the active session', () => {
    expect(capturedUrls.length).toBeGreaterThanOrEqual(1);
    expect(capturedUrls[0]).toMatch(/\/hubs\/shop-updates$/);
    expect(mockConnection.start).toHaveBeenCalled();
  });

  it('emits shop updates for the active shop only', async () => {
    await vi.waitFor(() => {
      expect(mockHandlers.has('ShopUpdated')).toBe(true);
    });

    const received = new Promise<ShopUpdatePayload>((resolve) => {
      const subscription = service.updates$.subscribe((payload) => {
        subscription.unsubscribe();
        resolve(payload);
      });
    });

    emitEvent('ShopUpdated', {
      eventType: 'PricingChanged',
      shopId: 'shop-xyz',
      changedIds: ['batch-1'],
      occurredOn: new Date().toISOString(),
    });

    await expect(received).resolves.toMatchObject({
      eventType: 'PricingChanged',
      shopId: 'shop-xyz',
      changedIds: ['batch-1'],
    });
  });

  it('filters out updates for other shops', async () => {
    await vi.waitFor(() => {
      expect(mockHandlers.has('ShopUpdated')).toBe(true);
    });

    let emissionCount = 0;
    const subscription = service.updates$.subscribe(() => {
      emissionCount += 1;
    });

    emitEvent('ShopUpdated', {
      eventType: 'PricingChanged',
      shopId: 'different-shop',
      changedIds: ['batch-1'],
      occurredOn: new Date().toISOString(),
    });

    subscription.unsubscribe();

    expect(emissionCount).toBe(0);
  });

  it('restarts the connection when the active shop changes', async () => {
    const startsBeforeChange = mockConnection.start.mock.calls.length;
    const urlsBeforeChange = capturedUrls.length;

    activeSession.set({ activeShopId: 'shop-abc', accessToken: 'test-token-2' });
    await service.startConnection();
    await vi.waitFor(() => {
      expect(mockConnection.stop).toHaveBeenCalled();
      expect(mockConnection.start.mock.calls.length).toBeGreaterThan(startsBeforeChange);
      expect(capturedUrls.length).toBeGreaterThan(urlsBeforeChange);
    });

    expect(mockConnection.stop).toHaveBeenCalled();
    expect(mockConnection.start.mock.calls.length).toBeGreaterThan(startsBeforeChange);
    expect(capturedUrls.length).toBeGreaterThan(urlsBeforeChange);
  });

  it('does not connect on a non-browser platform', async () => {
    TestBed.resetTestingModule();
    capturedUrls.length = 0;
    mockConnection.start.mockClear();

    TestBed.configureTestingModule({
      providers: [
        ShopUpdatesSignalRService,
        { provide: PLATFORM_ID, useValue: 'server' },
        { provide: AuthService, useValue: authService },
      ],
    });

    const serverService = TestBed.inject(ShopUpdatesSignalRService);
    await serverService.startConnection();

    expect(capturedUrls).toHaveLength(0);
    expect(mockConnection.start).not.toHaveBeenCalled();
  });
});
