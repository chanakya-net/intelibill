import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { PurchaseOrderDraftIndexedDbService, type PurchaseOrderDraftRecord } from '../../../core/storage/purchase-order-draft-indexeddb.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { SuppliersFacade } from '../../suppliers/state/suppliers.facade';
import type { PurchaseOrderDetail } from '../services/purchase-order.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { PurchaseOrderBuilderPageComponent } from './purchase-order-builder-page.component';

describe('PurchaseOrderBuilderPageComponent', () => {
  const selectedOrder = signal<PurchaseOrderDetail | null>(null);
  const suppliers = signal<readonly SupplierStub[]>([]);
  const session = signal({ activeShopId: 'shop-1' });
  const records = new Map<string, PurchaseOrderDraftRecord>();
  const facade = {
    selectedOrder,
    isSubmitting: signal(false),
    errorMessage: signal(''),
    loadDetail: vi.fn(),
    updateDraft: vi.fn(),
    createDraft: vi.fn(),
    clearDetail: vi.fn(),
  };
  const suppliersFacade = {
    suppliers,
    load: vi.fn(),
  };
  const storage = {
    loadDraft: vi.fn(async (shopId: string) => records.get(shopId) ?? null),
    saveDraft: vi.fn(async (record: PurchaseOrderDraftRecord) => {
      records.set(record.shopId, record);
    }),
    clearDraft: vi.fn(async (shopId: string) => {
      records.delete(shopId);
    }),
  };

  beforeEach(() => {
    selectedOrder.set(null);
    suppliers.set([]);
    records.clear();
    facade.loadDetail.mockReset();
    facade.updateDraft.mockReset();
    facade.createDraft.mockReset();
    facade.clearDetail.mockReset();
    suppliersFacade.load.mockReset();
    storage.loadDraft.mockClear();
    storage.saveDraft.mockClear();
    storage.clearDraft.mockClear();

    TestBed.configureTestingModule({
      imports: [PurchaseOrderBuilderPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (key: string) => key === 'purchaseOrderId' ? 'po-1' : null } } } },
        { provide: AuthService, useValue: { session } },
        { provide: PurchaseOrdersFacade, useValue: facade },
        { provide: SuppliersFacade, useValue: suppliersFacade },
        { provide: PurchaseOrderDraftIndexedDbService, useValue: storage },
        {
          provide: ProductCatalogSyncService,
          useValue: { filterByName: () => [], findByName: () => null, upsertEntry: vi.fn() },
        },
        {
          provide: InventoryService,
          useValue: { generateItemBarcode: () => of({ barcode: 'BAR-1' }), addItem: vi.fn() },
        },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('preserves supplier id when detail loads before supplier suggestions and another header field is autosaved', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderBuilderPageComponent);
    const component = fixture.componentInstance;
    await component.ngOnInit();

    (component as unknown as {
      draftState: { replaceFromServer: (order: PurchaseOrderDetail) => void };
    }).draftState.replaceFromServer(makeDetail());

    const supplier = (component as unknown as {
      resolveSupplier: (name: string) => { readonly id: string; readonly name: string } | null;
    }).resolveSupplier('');
    await (component as unknown as {
      draftState: { updateHeader: (shopId: string, update: unknown) => Promise<void> };
    }).draftState.updateHeader('shop-1', { supplier, notes: 'edited notes', purchaseOrderId: 'po-1' });

    component.saveDraft();

    expect(facade.updateDraft).toHaveBeenCalledWith('po-1', expect.objectContaining({
      supplierId: 'supplier-1',
      notes: 'edited notes',
    }));
  });
});

interface SupplierStub {
  readonly supplierId: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly isSystem: boolean;
}

function makeDetail(): PurchaseOrderDetail {
  return {
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-2026-000001',
    status: 'Draft',
    supplierId: 'supplier-1',
    orderDate: null,
    expectedDeliveryDate: null,
    supplierReferenceNumber: null,
    notes: 'server notes',
    lines: [],
    expectedTotal: 0,
    createdAt: '2026-06-01T00:00:00Z',
  };
}
