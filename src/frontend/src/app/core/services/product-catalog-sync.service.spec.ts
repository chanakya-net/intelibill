import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductCatalogSyncService } from './product-catalog-sync.service';
import { ProductCatalogIndexedDbService } from '../storage/product-catalog-indexeddb.service';
import { AuthService } from '../auth/auth.service';
import { ITEM_ENDPOINTS } from '../auth/auth.constants';
import { ShopContextService } from '../context/shop-context.service';
import { AppReloadService } from './app-reload.service';

describe('ProductCatalogSyncService - upsertEntry', () => {
  const mockShopId = 'shop-abc';
  let service: ProductCatalogSyncService;
  let saveCatalogSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    saveCatalogSpy = vi.fn().mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      providers: [
        ProductCatalogSyncService,
        {
          provide: AuthService,
          useValue: {
            session: signal({ activeShopId: mockShopId }),
            getAccessToken: () => 'tok',
          },
        },
        {
          provide: ProductCatalogIndexedDbService,
          useValue: {
            getCatalog: vi.fn().mockResolvedValue([]),
            saveCatalog: saveCatalogSpy,
          },
        },
      ],
    });
    service = TestBed.inject(ProductCatalogSyncService);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('appends new entry when barcode does not exist', () => {
    const entry = { itemId: 'i1', name: 'Rice', barcode: 'BAR001' };
    service.upsertEntry(entry);

    expect(service.catalogEntries()).toContainEqual(entry);
    expect(service.catalogEntries()).toHaveLength(1);
  });

  it('replaces existing entry with same barcode', () => {
    const original = { itemId: 'i1', name: 'Rice', barcode: 'BAR001' };
    const updated = { itemId: 'i1', name: 'Premium Rice', barcode: 'BAR001' };

    service.upsertEntry(original);
    service.upsertEntry(updated);

    const entries = service.catalogEntries();
    expect(entries).toHaveLength(1);
    expect(entries[0].name).toBe('Premium Rice');
  });

  it('multiple different barcodes accumulate independently', () => {
    service.upsertEntry({ itemId: 'i1', name: 'Rice', barcode: 'B1' });
    service.upsertEntry({ itemId: 'i2', name: 'Wheat', barcode: 'B2' });

    expect(service.catalogEntries()).toHaveLength(2);
  });

  it('persists to IndexedDB after upsert', async () => {
    const entry = { itemId: 'i1', name: 'Rice', barcode: 'BAR001' };
    service.upsertEntry(entry);

    await vi.waitFor(() => expect(saveCatalogSpy).toHaveBeenCalledWith(mockShopId, [entry]));
  });

  it('does not persist when no active shop', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        ProductCatalogSyncService,
        {
          provide: AuthService,
          useValue: {
            session: signal(null),
            getAccessToken: () => null,
          },
        },
        {
          provide: ProductCatalogIndexedDbService,
          useValue: {
            getCatalog: vi.fn().mockResolvedValue([]),
            saveCatalog: saveCatalogSpy,
          },
        },
      ],
    });
    const svc = TestBed.inject(ProductCatalogSyncService);

    svc.upsertEntry({ itemId: 'i1', name: 'Rice', barcode: 'BAR001' });

    expect(saveCatalogSpy).not.toHaveBeenCalled();
  });
});

describe('ProductCatalogSyncService - scope changes', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  it('drops a catalog stream that answers after the tab left the shop', async () => {
    const saveCatalog = vi.fn().mockResolvedValue(undefined);
    let answer!: (response: Response) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((resolve) => (answer = resolve))));
    TestBed.configureTestingModule({
      providers: [
        ProductCatalogSyncService,
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
        { provide: AuthService, useValue: { session: signal({ activeShopId: 'shop-a' }), getAccessToken: () => 'tok' } },
        { provide: ProductCatalogIndexedDbService, useValue: { getCatalog: vi.fn().mockResolvedValue([]), saveCatalog } },
      ],
    });
    const context = TestBed.inject(ShopContextService);
    context.initialize({ userId: 'user-1', shopId: 'shop-a' });
    const service = TestBed.inject(ProductCatalogSyncService);
    TestBed.tick();
    await vi.waitFor(() => expect(fetch).toHaveBeenCalled());

    context.transition({ userId: 'user-1', shopId: 'shop-b' }, 'cross-tab', () => undefined);
    answer(new Response('{"itemId":"i1","name":"Rice","barcode":"B1"}\n'));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(service.catalogEntries()).toEqual([]);
    expect(saveCatalog).not.toHaveBeenCalled();
  });

  it('streams the catalog around the service worker and the HTTP cache', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => undefined)));
    TestBed.configureTestingModule({
      providers: [
        ProductCatalogSyncService,
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
        { provide: AuthService, useValue: { session: signal({ activeShopId: 'shop-a' }), getAccessToken: () => 'tok' } },
        { provide: ProductCatalogIndexedDbService, useValue: { getCatalog: vi.fn().mockResolvedValue([]), saveCatalog: vi.fn() } },
      ],
    });
    TestBed.inject(ShopContextService).initialize({ userId: 'user-1', shopId: 'shop-a' });
    TestBed.inject(ProductCatalogSyncService);
    TestBed.tick();
    await vi.waitFor(() => expect(fetch).toHaveBeenCalled());

    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${ITEM_ENDPOINTS.stream}?ngsw-bypass=true`);
    expect(init.cache).toBe('no-store');
    expect(init.headers).toEqual({ Authorization: 'Bearer tok' });
  });
});

