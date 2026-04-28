import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductCatalogSyncService } from './product-catalog-sync.service';
import { ProductCatalogIndexedDbService } from '../storage/product-catalog-indexeddb.service';
import { AuthService } from '../auth/auth.service';

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
