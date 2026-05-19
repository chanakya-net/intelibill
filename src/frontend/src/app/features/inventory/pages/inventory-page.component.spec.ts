import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Action } from '@ngrx/store';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { InventoryActions } from '../state/inventory.actions';
import {
  selectInventoryErrorMessage,
  selectInventoryItems,
  selectInventoryLastMutationSucceeded,
  selectInventoryLastMutationType,
  selectInventoryLoadingItems,
  selectInventorySubmitting,
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

  const itemsSignal = signal([
    {
      id: 'item-1',
      name: 'Milk',
      barcode: 'B001',
      description: null,
      uom: 'ltr',
      isActive: true,
      currentStock: 10,
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

      return signal(null);
    }),
  };

  store.dispatch.mockImplementation((action: Action) => {
    if (action.type === InventoryActions.clearMutationStatus.type) {
      lastMutationTypeSignal.set(null);
      lastMutationSucceededSignal.set(false);
    }

    if (action.type === InventoryActions.clearError.type) {
      errorSignal.set('');
    }
  });

  const sessionSignal = signal(ownerSession);

  const authService = {
    session: sessionSignal,
  };

  beforeEach(() => {
    store.dispatch.mockReset();
    itemsSignal.set([
      {
        id: 'item-1',
        name: 'Milk',
        barcode: 'B001',
        description: null,
        uom: 'ltr',
        isActive: true,
        currentStock: 10,
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
    sessionSignal.set(ownerSession);

    TestBed.configureTestingModule({
      imports: [InventoryPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: Store, useValue: store },
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  afterEach(() => {
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

    expect(store.dispatch).toHaveBeenCalledWith(InventoryActions.loadItemsRequested());
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

  it('filters mobile list when search value changes', () => {
    const fixture = TestBed.createComponent(InventoryPageComponent);
    const component = fixture.componentInstance as unknown as {
      searchValue: { set: (value: string) => void; (): string };
      filteredItems: () => Array<{ id: string; name: string }>;
    };

    component.searchValue.set('milk');

    expect(component.filteredItems().map((item) => item.name)).toEqual(['Milk']);
  });
});
