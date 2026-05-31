import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpResponse } from '@angular/common/http';
import { Action } from '@ngrx/store';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';
import { of } from 'rxjs';
import { ConfirmationService } from 'primeng/api';

import { AuthService } from '../../../core/auth/auth.service';
import { InventoryService } from '../services/inventory.service';
import { BlobDownloadService } from '../../../shared/utils/blob-download.service';
import type { Item } from '../services/inventory.models';
import { InventoryActions } from '../state/inventory.actions';
import {
  selectInventoryErrorMessage,
  selectInventoryItems,
  selectInventoryLastAddedItem,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventoryLoadingItems,
  selectInventorySubmitting,
  selectInventoryPagination,
  selectInventorySummary,
} from '../state/inventory.selectors';
import { InventoryPageComponent } from './inventory-page.component';

describe('InventoryPageComponent', () => {
  const ownerSession = {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
    rememberMe: true,
    user: {
      id: 'owner-1',
      email: 'owner@test.com',
      phoneNumber: null,
      firstName: 'Owner',
      lastName: 'One',
    },
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
  };

  const itemsSignal = signal<Item[]>([
    {
      id: 'item-1',
      name: 'Milk',
      barcode: 'B001',
      description: null,
      uom: 'ltr',
      isActive: true,
      currentStock: 10,
      unitPrice: 45,
      currentStockValue: 450,
      reorderLevel: 5,
      stockStatus: 'inStock',
      hsnCode: '0401',
      defaultTaxRatePercent: 5,
      defaultTaxIncluded: false,
    },
  ]);
  const submittingSignal = signal(false);
  const loadingItemsSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'add-item' | 'update-item' | null>(null);
  const lastMutationSucceededSignal = signal(false);
  const lastAddedItemSignal = signal<Item | null>(null);
  const paginationSignal = signal({ totalCount: 1, pageNumber: 1, pageSize: 20 });
  const summarySignal = signal({
    totalItems: 1,
    activeItems: 1,
    inactiveItems: 0,
    runningLowStockCount: 0,
    criticalStockCount: 0,
    totalStockValue: 450,
  });

  const store = {
    dispatch: vi.fn(),
    selectSignal: vi.fn((selector: unknown) => {
      if (selector === selectInventoryItems) {
        return itemsSignal;
      }

      if (selector === selectInventorySubmitting) {
        return submittingSignal;
      }

      if (selector === selectInventoryLoadingItems) {
        return loadingItemsSignal;
      }

      if (selector === selectInventoryErrorMessage) {
        return errorSignal;
      }

      if (selector === selectInventoryLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectInventoryLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      if (selector === selectInventoryLastAddedItem) {
        return lastAddedItemSignal;
      }

      if (selector === selectInventoryPagination) {
        return paginationSignal;
      }

      if (selector === selectInventorySummary) {
        return summarySignal;
      }

      return signal(null);
    }),
  };

  store.dispatch.mockImplementation((action: Action) => {
    if (action.type === InventoryActions.clearMutationStatus.type) {
      lastMutationTypeSignal.set(null);
      lastMutationSucceededSignal.set(false);
      lastAddedItemSignal.set(null);
    }

    if (action.type === InventoryActions.clearError.type) {
      errorSignal.set('');
    }
  });

  const sessionSignal = signal(ownerSession);

  const authService = {
    session: sessionSignal,
  };

  const inventoryService = {
    printBarcodeLabels: vi.fn(),
  };

  const blobDownloadService = {
    openPdf: vi.fn(),
    download: vi.fn(),
  };

  beforeEach(() => {
    store.dispatch.mockReset();
    inventoryService.printBarcodeLabels.mockReset();
    blobDownloadService.openPdf.mockReset();
    blobDownloadService.download.mockReset();
    itemsSignal.set([
      {
        id: 'item-1',
        name: 'Milk',
        barcode: 'B001',
        description: null,
        uom: 'ltr',
        isActive: true,
        currentStock: 10,
        unitPrice: 45,
        currentStockValue: 450,
        reorderLevel: 5,
        stockStatus: 'inStock',
        hsnCode: '0401',
        defaultTaxRatePercent: 5,
        defaultTaxIncluded: false,
      },
    ]);
    submittingSignal.set(false);
    loadingItemsSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
    lastAddedItemSignal.set(null);
    sessionSignal.set(ownerSession);

    TestBed.configureTestingModule({
      imports: [InventoryPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: Store, useValue: store },
        { provide: AuthService, useValue: authService },
        { provide: InventoryService, useValue: inventoryService },
        { provide: BlobDownloadService, useValue: blobDownloadService },
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('opens add overlay for owner/manager', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;

    component.onOpenAddProduct();

    expect(component.showAddProductOverlay()).toBe(true);
  });

  it('dispatches load items when page initializes', () => {
    TestBed.createComponent(InventoryPageComponent);

    expect(store.dispatch).toHaveBeenCalledWith(
      InventoryActions.loadItemsRequested({
        query: {
          search: '',
          status: 'all',
          pageNumber: 1,
          pageSize: 20,
        },
      })
    );
  });

  it('closes add overlay on successful add mutation', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;

    component.onOpenAddProduct();
    expect(component.showAddProductOverlay()).toBe(true);

    lastMutationTypeSignal.set('add-item');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.showAddProductOverlay()).toBe(false);
    expect(store.dispatch).toHaveBeenCalledWith(InventoryActions.clearMutationStatus());
  });

  it('does not allow staff to open add overlay', () => {
    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    });

    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;

    component.onOpenAddProduct();

    expect(component.canManageInventory()).toBe(false);
    expect(component.showAddProductOverlay()).toBe(false);
  });

  it('opens edit overlay for owner/manager', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    const item = itemsSignal()[0];

    component.onEditItem(item);

    expect(component.showEditItemOverlay()).toBe(true);
    expect(component.selectedItemForEdit()).toEqual(item);
  });

  it('does not allow staff to open edit overlay', () => {
    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    });

    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    const item = itemsSignal()[0];

    component.onEditItem(item);

    expect(component.canManageInventory()).toBe(false);
    expect(component.showEditItemOverlay()).toBe(false);
  });

  it('closes edit overlay when requested', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    const item = itemsSignal()[0];

    component.onEditItem(item);
    expect(component.showEditItemOverlay()).toBe(true);

    component.onCloseEditItem();

    expect(component.showEditItemOverlay()).toBe(false);
  });

  it('dispatches page query after debounced search change and resets to first page', () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;

    component.pageNumber.set(3);
    store.dispatch.mockClear();
    component.onSearchChange('milk');
    expect(store.dispatch).not.toHaveBeenCalled();

    vi.advanceTimersByTime(279);
    expect(store.dispatch).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(component.pageNumber()).toBe(1);
    expect(store.dispatch).toHaveBeenCalledWith(
      InventoryActions.loadItemsRequested({
        query: {
          search: 'milk',
          status: 'all',
          pageNumber: 1,
          pageSize: 20,
        },
      })
    );
  });

  it('resets page to first when status filter changes', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    component.pageNumber.set(4);
    store.dispatch.mockClear();

    component.onStatusFilterChange('inStock');

    expect(component.pageNumber()).toBe(1);
    expect(store.dispatch).toHaveBeenCalledWith(
      InventoryActions.loadItemsRequested({
        query: {
          search: '',
          status: 'inStock',
          pageNumber: 1,
          pageSize: 20,
        },
      })
    );
  });

  it('resets page to first when page size changes', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    component.pageNumber.set(3);
    store.dispatch.mockClear();

    component.onPageSizeChange(25);

    expect(component.pageSize()).toBe(25);
    expect(component.pageNumber()).toBe(1);
    expect(store.dispatch).toHaveBeenCalledWith(
      InventoryActions.loadItemsRequested({
        query: {
          search: '',
          status: 'all',
          pageNumber: 1,
          pageSize: 25,
        },
      })
    );
  });

  it('renders inventory summary cards from selectors', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('en.inventory.activeProducts');
    expect(host.textContent).toContain('1 / 1');
    expect(host.textContent).toContain('en.inventory.currentStockValue');
    expect(host.textContent).toContain('450');
    expect(host.textContent).toContain('en.inventory.lowStock');
    expect(host.textContent).toContain('0');
  });

  it('closes edit overlay on successful update mutation', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    const item = itemsSignal()[0];

    component.onEditItem(item);
    expect(component.showEditItemOverlay()).toBe(true);

    lastMutationTypeSignal.set('update-item');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.showEditItemOverlay()).toBe(false);
    expect(component.selectedItemForEdit()).toBe(null);
    expect(store.dispatch).toHaveBeenCalledWith(InventoryActions.clearMutationStatus());
  });

  it('does not close edit overlay when submitting', () => {
    submittingSignal.set(true);
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;

    component.showEditItemOverlay.set(true);
    component.onCloseEditItem();

    expect(component.showEditItemOverlay()).toBe(true);
  });

  it('renders summary footer values from pagination state', () => {
    paginationSignal.set({ totalCount: 58, pageNumber: 2, pageSize: 20 });
    const fixture = TestBed.createComponent(InventoryPageComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('en.inventory.paginationFooter');
  });

  it('opens barcode label print dialog for selected items', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    const item = itemsSignal()[0];

    component.onCatalogSelectionChange([item]);
    component.onPrintSelectedLabels();

    expect(component.printDialogVisible()).toBe(true);
    expect(component.printDialogCandidates()).toEqual([
      {
        itemId: item.id,
        itemName: item.name,
        barcode: item.barcode,
        inventoryBatchId: null,
      },
    ]);
  });

  it('constructs print request and opens PDF blob response', () => {
    const blob = new Blob(['labels'], { type: 'application/pdf' });
    inventoryService.printBarcodeLabels.mockReturnValue(of(new HttpResponse({ status: 200, body: blob })));

    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    component.printDialogVisible.set(true);

    component.onPrintDialogRequested({
      items: [{ itemId: 'item-1', quantity: 1, inventoryBatchId: null }],
    });

    expect(inventoryService.printBarcodeLabels).toHaveBeenCalledWith({
      items: [{ itemId: 'item-1', quantity: 1, inventoryBatchId: null }],
    });
    expect(blobDownloadService.openPdf).toHaveBeenCalledWith(blob);
    expect(component.printDialogVisible()).toBe(false);
  });

  it('falls back to downloading when PDF open fails', () => {
    const blob = new Blob(['labels'], { type: 'application/pdf' });
    inventoryService.printBarcodeLabels.mockReturnValue(of(new HttpResponse({ status: 200, body: blob })));
    blobDownloadService.openPdf.mockImplementation(() => {
      throw new Error('popup blocked');
    });

    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    component.printDialogVisible.set(true);

    component.onPrintDialogRequested({
      items: [{ itemId: 'item-1', quantity: 1, inventoryBatchId: null }],
    });

    expect(blobDownloadService.download).toHaveBeenCalledWith(blob, 'barcode-labels.pdf');
  });

  it('prompts to print barcode after add item succeeds', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance;
    const confirmationService = fixture.debugElement.injector.get(ConfirmationService);
    const confirmSpy = vi.spyOn(confirmationService, 'confirm');

    component.onOpenAddProduct();
    lastAddedItemSignal.set(itemsSignal()[0]);
    lastMutationTypeSignal.set('add-item');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(confirmSpy).toHaveBeenCalled();
    const call = confirmSpy.mock.calls[0][0];
    expect(call.message).toBe('inventory.printBarcode.prompt.message');
    expect(call.header).toBe('inventory.printBarcode.prompt.header');
    expect(typeof call.accept).toBe('function');
  });
});
