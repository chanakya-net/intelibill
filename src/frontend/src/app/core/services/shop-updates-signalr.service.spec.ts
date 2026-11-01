import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { firstValueFrom, timeout } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { HubConnectionFactory } from './hub-connection.factory';
import { ShopUpdatePayload, ShopUpdatesSignalRService } from './shop-updates-signalr.service';
import {
  FakeHubConnection,
  FakeHubConnectionFactory,
} from '../../../tests/realtime/fake-hub-connection.factory';

// This suite was skipped for a pipeline-only race while it replaced the `@microsoft/signalr`
// module: that only works when this file is evaluated before anything else imports the library.
// Substituting HubConnectionFactory has no such ordering requirement.
describe('ShopUpdatesSignalRService', () => {
  const activeShopId = 'shop-xyz';

  let session: ReturnType<typeof signal<{ activeShopId: string | null; accessToken: string } | null>>;
  let factory: FakeHubConnectionFactory;
  let service: ShopUpdatesSignalRService;

  function configure(sessionValue: { activeShopId: string | null; accessToken: string } | null): void {
    session = signal(sessionValue);
    factory = new FakeHubConnectionFactory();

    TestBed.configureTestingModule({
      providers: [
        ShopUpdatesSignalRService,
        { provide: HubConnectionFactory, useValue: factory },
        {
          provide: AuthService,
          useValue: {
            session,
            getAccessToken: () => session()?.accessToken ?? null,
          },
        },
      ],
    });
  }

  function update(shopId: string): ShopUpdatePayload {
    return {
      eventType: 'PricingChanged',
      shopId,
      changedIds: ['batch-1'],
      occurredOn: new Date().toISOString(),
    };
  }

  function emitOn(connection: FakeHubConnection, shopId: string): void {
    connection.emit('ShopUpdated', update(shopId));
  }

  beforeEach(async () => {
    configure({ activeShopId, accessToken: 'test-token' });
    service = TestBed.inject(ShopUpdatesSignalRService);
    await service.startConnection();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.clearAllMocks();
  });

  it('connects to the shop updates hub for the active session', () => {
    expect(factory.urls).toHaveLength(1);
    expect(factory.urls[0]).toMatch(/\/hubs\/shop-updates$/);
    expect(factory.latest.startCalls).toBe(1);
  });

  it('emits shop updates for the active shop', async () => {
    const received = firstValueFrom(service.updates$);

    emitOn(factory.latest, activeShopId);

    await expect(received).resolves.toMatchObject({
      eventType: 'PricingChanged',
      shopId: activeShopId,
      changedIds: ['batch-1'],
    });
  });

  it('filters out updates for other shops', () => {
    let emissions = 0;
    const subscription = service.updates$.subscribe(() => (emissions += 1));

    emitOn(factory.latest, 'different-shop');
    subscription.unsubscribe();

    expect(emissions).toBe(0);
  });

  it('restarts the connection when the active shop changes', async () => {
    const previous = factory.latest;

    session.set({ activeShopId: 'shop-abc', accessToken: 'test-token-2' });
    await service.startConnection();

    expect(factory.created).toHaveLength(2);
    expect(previous.stopCalls).toBe(1);
    expect(factory.urls).toHaveLength(2);
  });

  it('ShopSwitch_DelayedOldCallback_IsIgnored', async () => {
    // The frame already in flight when the shop changed. Publishing it would redraw the new shop's
    // pricing from the previous shop's connection.
    const previous = factory.latest;

    session.set({ activeShopId: 'shop-abc', accessToken: 'test-token-2' });
    await service.startConnection();

    let emissions = 0;
    const subscription = service.updates$.subscribe(() => (emissions += 1));

    emitOn(previous, activeShopId);
    emitOn(previous, 'shop-abc');
    expect(emissions).toBe(0);

    // The replacement is live.
    emitOn(factory.latest, 'shop-abc');
    expect(emissions).toBe(1);

    subscription.unsubscribe();
  });

  it('stops emitting once the connection has been stopped', async () => {
    const connection = factory.latest;
    let emissions = 0;
    const subscription = service.updates$.subscribe(() => (emissions += 1));

    await service.stopConnection();
    emitOn(connection, activeShopId);

    expect(emissions).toBe(0);
    subscription.unsubscribe();
  });

  it('does not emit once the session has lost its active shop', () => {
    const connection = factory.latest;
    let emissions = 0;
    const subscription = service.updates$.subscribe(() => (emissions += 1));

    session.set(null);
    emitOn(connection, activeShopId);

    expect(emissions).toBe(0);
    subscription.unsubscribe();
  });

  it('SessionRevoked_StopsAutomaticReconnect', () => {
    const policy = factory.retryPolicies[0];

    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 1,
        elapsedMilliseconds: 10,
        retryReason: { statusCode: 401 },
      }),
    ).toBeNull();
  });

  it('keeps retrying a network interruption', () => {
    const policy = factory.retryPolicies[0];

    expect(
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount: 2,
        elapsedMilliseconds: 12_000,
        retryReason: new Error('Failed to fetch'),
      }),
    ).toBeGreaterThan(0);
  });

  it('does not open a connection without an active session', async () => {
    TestBed.resetTestingModule();
    configure(null);
    const svc = TestBed.inject(ShopUpdatesSignalRService);

    await svc.startConnection();

    expect(factory.created).toHaveLength(0);
    await expect(firstValueFrom(svc.updates$.pipe(timeout(20)))).rejects.toThrow();
  });
});
