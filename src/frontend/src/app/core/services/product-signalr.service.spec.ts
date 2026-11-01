import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductSignalRService } from './product-signalr.service';
import { ProductCatalogSyncService } from './product-catalog-sync.service';
import { HubConnectionFactory } from './hub-connection.factory';
import { AuthService } from '../auth/auth.service';
import {
  FakeHubConnection,
  FakeHubConnectionFactory,
} from '../../../tests/realtime/fake-hub-connection.factory';

describe('ProductSignalRService', () => {
  const activeShopId = 'shop-xyz';

  let session: ReturnType<typeof signal<{ activeShopId: string | null; accessToken: string } | null>>;
  let factory: FakeHubConnectionFactory;
  let upsertEntry: ReturnType<typeof vi.fn>;
  let service: ProductSignalRService;

  function configure(sessionValue: { activeShopId: string | null; accessToken: string } | null): void {
    session = signal(sessionValue);
    factory = new FakeHubConnectionFactory();
    upsertEntry = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        ProductSignalRService,
        { provide: HubConnectionFactory, useValue: factory },
        {
          provide: AuthService,
          useValue: {
            session,
            getAccessToken: () => session()?.accessToken ?? null,
          },
        },
        { provide: ProductCatalogSyncService, useValue: { upsertEntry } },
      ],
    });
  }

  function productAdded(shopId: string, itemId = 'item-1'): void {
    factory.latest.emit('ProductAdded', { itemId, barcode: 'BAR001', name: 'Rice', shopId });
  }

  function emitOn(connection: FakeHubConnection, shopId: string): void {
    connection.emit('ProductAdded', { itemId: 'stale', barcode: 'B9', name: 'Stale', shopId });
  }

  beforeEach(async () => {
    configure({ activeShopId, accessToken: 'test-token' });
    service = TestBed.inject(ProductSignalRService);
    await service.startConnection();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.clearAllMocks();
  });

  it('calls upsertEntry when ProductAdded matches the active shop', () => {
    productAdded(activeShopId);

    expect(upsertEntry).toHaveBeenCalledOnce();
    expect(upsertEntry).toHaveBeenCalledWith({
      itemId: 'item-1',
      barcode: 'BAR001',
      name: 'Rice',
    });
  });

  it('does NOT call upsertEntry when shopId does not match the active shop', () => {
    productAdded('different-shop-id');

    expect(upsertEntry).not.toHaveBeenCalled();
  });

  it('does NOT call upsertEntry when payload shopId is empty', () => {
    productAdded('');

    expect(upsertEntry).not.toHaveBeenCalled();
  });

  it('connects to an absolute backend URL containing /hubs/products', () => {
    expect(factory.urls).toHaveLength(1);
    expect(factory.urls[0]).toMatch(/\/hubs\/products$/);
    expect(factory.urls[0]).not.toBe('/hubs/products');
    expect(factory.latest.startCalls).toBe(1);
  });

  it('does not open a connection when there is no active session', async () => {
    TestBed.resetTestingModule();
    configure(null);
    const svc = TestBed.inject(ProductSignalRService);

    await svc.startConnection();

    expect(factory.created).toHaveLength(0);
    expect(upsertEntry).not.toHaveBeenCalled();
  });

  it('does not rebuild the connection when started twice for the same session', async () => {
    await service.startConnection();

    expect(factory.created).toHaveLength(1);
  });

  it('rebuilds the connection when the access token is renewed', async () => {
    const previous = factory.latest;

    session.set({ activeShopId, accessToken: 'renewed-token' });
    await service.startConnection();

    expect(factory.created).toHaveLength(2);
    expect(previous.stopCalls).toBe(1);
  });

  it('stops the connection cleanly', async () => {
    const connection = factory.latest;

    await service.stopConnection();

    expect(connection.stopCalls).toBe(1);
  });

  it('does not throw when stopConnection is called before startConnection', async () => {
    TestBed.resetTestingModule();
    configure({ activeShopId, accessToken: 'test-token' });
    const svc = TestBed.inject(ProductSignalRService);

    await expect(svc.stopConnection()).resolves.toBeUndefined();
  });

  it('ShopSwitch_DelayedOldCallback_IsIgnored', async () => {
    // A frame already in flight when the shop changed. Its socket has been replaced, so neither the
    // previous shop's id nor the new one may reach the catalog through it: the previous shop's data
    // does not belong in the new shop's view, and the new shop's data did not arrive on a connection
    // authorized for it.
    const previous = factory.latest;

    session.set({ activeShopId: 'shop-abc', accessToken: 'token-abc' });
    await service.startConnection();

    emitOn(previous, activeShopId);
    emitOn(previous, 'shop-abc');
    expect(upsertEntry).not.toHaveBeenCalled();

    // The replacement, in contrast, is live.
    productAdded('shop-abc', 'fresh');
    expect(upsertEntry).toHaveBeenCalledOnce();
  });

  it('ignores a callback that arrives after the connection was stopped', async () => {
    const connection = factory.latest;

    await service.stopConnection();
    emitOn(connection, activeShopId);

    expect(upsertEntry).not.toHaveBeenCalled();
  });

  it('SessionRevoked_StopsAutomaticReconnect', () => {
    const policy = factory.retryPolicies[0];

    // A refused handshake: another attempt cannot change the answer.
    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 1,
        elapsedMilliseconds: 10,
        retryReason: { statusCode: 401 },
      }),
    ).toBeNull();

    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 1,
        elapsedMilliseconds: 10,
        retryReason: { statusCode: 403 },
      }),
    ).toBeNull();
  });

  it('keeps retrying a network interruption', () => {
    const policy = factory.retryPolicies[0];

    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 0,
        elapsedMilliseconds: 10,
        retryReason: new Error('Server timeout elapsed without receiving a message.'),
      }),
    ).toBe(0);

    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 1,
        elapsedMilliseconds: 2_000,
        retryReason: new Error('Failed to fetch'),
      }),
    ).toBeGreaterThan(0);
  });

  it('stops retrying once the session is gone entirely', () => {
    const policy = factory.retryPolicies[0];
    session.set(null);

    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 0,
        elapsedMilliseconds: 10,
        retryReason: new Error('Failed to fetch'),
      }),
    ).toBeNull();
  });

  it('discards a connection whose handshake was refused', async () => {
    TestBed.resetTestingModule();
    configure({ activeShopId, accessToken: 'test-token' });
    const svc = TestBed.inject(ProductSignalRService);
    const failing = new FakeHubConnectionFactory();
    vi.spyOn(factory, 'create').mockImplementation((request) => {
      const connection = failing.create(request) as FakeHubConnection;
      connection.startFailure = Object.assign(new Error('Unauthorized'), { statusCode: 401 });
      return connection;
    });

    await svc.startConnection();

    // Nothing is retained, so the next session change starts from a clean slate rather than from a
    // socket that never opened.
    emitOn(failing.latest, activeShopId);
    expect(upsertEntry).not.toHaveBeenCalled();
  });
});
