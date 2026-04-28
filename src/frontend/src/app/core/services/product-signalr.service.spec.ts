import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductSignalRService } from './product-signalr.service';
import { ProductCatalogSyncService } from './product-catalog-sync.service';
import { AuthService } from '../auth/auth.service';

const { mockHandlers, mockConnection, capturedUrls } = vi.hoisted(() => {
  const handlers = new Map<string, (payload: unknown) => void>();
  const urls: string[] = [];
  const conn = {
    on: (event: string, handler: (payload: unknown) => void) => handlers.set(event, handler),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
  };
  return { mockHandlers: handlers, mockConnection: conn, capturedUrls: urls };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(url: string) { capturedUrls.push(url); return this; }
    withAutomaticReconnect() { return this; }
    build() { return mockConnection; }
  },
}));

describe('ProductSignalRService', () => {
  const activeShopId = 'shop-xyz';
  let service: ProductSignalRService;
  let upsertEntrySpy: ReturnType<typeof vi.fn>;

  function emitEvent(event: string, payload: unknown): void {
    mockHandlers.get(event)?.(payload);
  }

  beforeEach(async () => {
    upsertEntrySpy = vi.fn();
    mockHandlers.clear();
    capturedUrls.length = 0;
    mockConnection.start.mockResolvedValue(undefined);
    mockConnection.stop.mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      providers: [
        ProductSignalRService,
        {
          provide: AuthService,
          useValue: {
            session: signal({ activeShopId }),
            getAccessToken: () => 'test-token',
          },
        },
        {
          provide: ProductCatalogSyncService,
          useValue: { upsertEntry: upsertEntrySpy },
        },
      ],
    });
    service = TestBed.inject(ProductSignalRService);
    await service.startConnection();
  });

  afterEach(() => {
    vi.clearAllMocks();
    TestBed.resetTestingModule();
  });

  it('calls upsertEntry when ProductAdded event matches active shop', () => {
    emitEvent('ProductAdded', {
      itemId: 'item-1',
      barcode: 'BAR001',
      name: 'Rice',
      shopId: activeShopId,
    });

    expect(upsertEntrySpy).toHaveBeenCalledOnce();
    expect(upsertEntrySpy).toHaveBeenCalledWith({
      itemId: 'item-1',
      barcode: 'BAR001',
      name: 'Rice',
    });
  });

  it('does NOT call upsertEntry when shopId does not match active shop', () => {
    emitEvent('ProductAdded', {
      itemId: 'item-2',
      barcode: 'BAR002',
      name: 'Wheat',
      shopId: 'different-shop-id',
    });

    expect(upsertEntrySpy).not.toHaveBeenCalled();
  });

  it('does NOT call upsertEntry when payload shopId is empty', () => {
    emitEvent('ProductAdded', { itemId: 'i3', barcode: 'B3', name: 'Salt', shopId: '' });

    expect(upsertEntrySpy).not.toHaveBeenCalled();
  });

  it('does NOT call upsertEntry when no active session', async () => {
    const noSessionUpsert = vi.fn();
    TestBed.resetTestingModule();
    mockHandlers.clear();

    TestBed.configureTestingModule({
      providers: [
        ProductSignalRService,
        {
          provide: AuthService,
          useValue: { session: signal(null), getAccessToken: () => null },
        },
        {
          provide: ProductCatalogSyncService,
          useValue: { upsertEntry: noSessionUpsert },
        },
      ],
    });
    const svc = TestBed.inject(ProductSignalRService);
    await svc.startConnection();

    emitEvent('ProductAdded', { itemId: 'i4', barcode: 'B4', name: 'Sugar', shopId: activeShopId });

    expect(noSessionUpsert).not.toHaveBeenCalled();
  });

  it('does not throw when startConnection called twice', async () => {
    await expect(service.startConnection()).resolves.toBeUndefined();
  });

  it('stops connection cleanly', async () => {
    await expect(service.stopConnection()).resolves.toBeUndefined();
  });

  it('connects to absolute backend URL containing /hubs/products', () => {
    expect(capturedUrls).toHaveLength(1);
    expect(capturedUrls[0]).toMatch(/\/hubs\/products$/);
    expect(capturedUrls[0]).not.toBe('/hubs/products');
  });

  it('does not throw when stopConnection called before startConnection', async () => {
    TestBed.resetTestingModule();
    mockHandlers.clear();
    TestBed.configureTestingModule({
      providers: [
        ProductSignalRService,
        {
          provide: AuthService,
          useValue: { session: signal({ activeShopId }), getAccessToken: () => 'tok' },
        },
        {
          provide: ProductCatalogSyncService,
          useValue: { upsertEntry: vi.fn() },
        },
      ],
    });
    const freshSvc = TestBed.inject(ProductSignalRService);
    await expect(freshSvc.stopConnection()).resolves.toBeUndefined();
  });
});
