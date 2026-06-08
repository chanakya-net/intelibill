import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
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
  const canManagePurchaseOrders = signal(true);
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
    canManagePurchaseOrders.set(true);
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
        provideRouter([
          { path: 'inventory/purchase-orders', redirectTo: '' },
          { path: 'inventory/purchase-orders/:purchaseOrderId', redirectTo: '' }
        ]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (key: string) => key === 'purchaseOrderId' ? 'po-1' : null } } } },
        { provide: AuthService, useValue: { session } },
        { provide: ShopPermissionsService, useValue: { canManagePurchaseOrders } },
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
    component.purchaseOrderId = 'po-1';
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

  it('ignores a saved local draft from another purchase order on an edit route', async () => {
    records.set('shop-1', {
      shopId: 'shop-1',
      purchaseOrderId: 'po-2',
      supplier: null,
      orderDate: null,
      expectedDeliveryDate: null,
      supplierReferenceNumber: null,
      notes: 'wrong draft',
      lines: [{ itemId: 'item-2', description: 'Other', expectedQuantity: 1, unitCost: 5 }],
      updatedAt: '2026-06-01T00:00:00Z',
    });

    const fixture = TestBed.createComponent(PurchaseOrderBuilderPageComponent);
    fixture.componentInstance.purchaseOrderId = 'po-1';
    await fixture.componentInstance.ngOnInit();

    const draftState = (fixture.componentInstance as unknown as {
      draftState: { header: () => { purchaseOrderId: string | null; notes: string | null }; lines: () => readonly unknown[] };
    }).draftState;

    expect(storage.clearDraft).toHaveBeenCalledWith('shop-1');
    expect(draftState.header().purchaseOrderId).toBeNull();
    expect(draftState.header().notes).toBeNull();
    expect(draftState.lines()).toEqual([]);
    expect(facade.loadDetail).toHaveBeenCalledWith('po-1');
  });

  it('hydrates an edit draft from the selected order only once', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderBuilderPageComponent);
    fixture.componentInstance.purchaseOrderId = 'po-1';
    await fixture.componentInstance.ngOnInit();

    const draftState = (fixture.componentInstance as unknown as {
      draftState: { replaceFromServer: (order: PurchaseOrderDetail) => void };
    }).draftState;
    const replaceFromServerSpy = vi.spyOn(draftState, 'replaceFromServer');

    selectedOrder.set(makeDetail());
    fixture.detectChanges();

    expect(replaceFromServerSpy).toHaveBeenCalledTimes(1);
  });

  it('does not rehydrate repeatedly after resolving the supplier name', async () => {
    suppliers.set([
      { supplierId: 'supplier-1', name: 'Acme Traders', isActive: true, isSystem: false },
    ]);
    const fixture = TestBed.createComponent(PurchaseOrderBuilderPageComponent);
    fixture.componentInstance.purchaseOrderId = 'po-1';
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));

    const draftState = (fixture.componentInstance as unknown as {
      draftState: {
        header: () => { supplier: { readonly id: string; readonly name: string } | null };
        replaceFromServer: (order: PurchaseOrderDetail) => void;
      };
    }).draftState;
    const replaceFromServerSpy = vi.spyOn(draftState, 'replaceFromServer');

    selectedOrder.set(makeDetail());
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));

    expect(replaceFromServerSpy).toHaveBeenCalledTimes(1);
    expect(draftState.header().supplier).toEqual({ id: 'supplier-1', name: 'Acme Traders' });
  });

  it('redirects to list when Staff tries to access builder', async () => {
    canManagePurchaseOrders.set(false);
    const fixture = TestBed.createComponent(PurchaseOrderBuilderPageComponent);
    await fixture.componentInstance.ngOnInit();

    expect(facade.loadDetail).not.toHaveBeenCalled();
    expect(storage.loadDraft).not.toHaveBeenCalled();
  });

  it('renders the builder with PrimeNG form and table controls', async () => {
    const fixture = TestBed.createComponent(PurchaseOrderBuilderPageComponent);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('p-autocomplete')).toBeTruthy();
    expect(host.querySelectorAll('p-datepicker').length).toBe(2);
    expect(host.querySelector('p-inputnumber')).toBeTruthy();
    expect(host.querySelector('p-table')).toBeTruthy();
    expect(host.querySelector('textarea[ptextarea]')).toBeTruthy();
  });

  it('updates supplier autocomplete suggestions from the query', async () => {
    suppliers.set([
      { supplierId: 'supplier-1', name: 'Acme Traders', isActive: true, isSystem: false },
      { supplierId: 'supplier-2', name: 'Beta Wholesale', isActive: true, isSystem: false },
      { supplierId: 'supplier-3', name: 'System Supplier', isActive: true, isSystem: true },
    ]);
    const fixture = TestBed.createComponent(PurchaseOrderBuilderPageComponent);
    await fixture.componentInstance.ngOnInit();

    fixture.componentInstance['onSupplierSearch']({ query: 'ac' } as never);

    const suggestions = (fixture.componentInstance as unknown as {
      supplierNameSuggestions: () => string[];
    }).supplierNameSuggestions();
    expect(suggestions).toEqual(['Acme Traders']);
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
    supplierName: 'Acme Traders',
    supplierReference: 'SUP-REF-001',
    receivedQuantity: 0,
    orderDate: null,
    expectedDeliveryDate: null,
    supplierReferenceNumber: null,
    notes: 'server notes',
    lines: [],
    expectedTotal: 0,
    createdAt: '2026-06-01T00:00:00Z',
    cancellationReason: null,
  };
}
