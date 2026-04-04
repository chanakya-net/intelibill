import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
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
  const itemsSignal = signal([
    {
      itemId: 'item-1',
      name: 'Milk',
      barcode: 'B001',
      description: null,
      uom: 'ltr',
      isActive: true,
      preferredSupplierId: null,
    },
  ]);
  const submittingSignal = signal(false);
  const loadingItemsSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'add-item' | null>(null);
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

  const sessionSignal = signal({
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
  });

  const authService = {
    session: sessionSignal,
  };

  beforeEach(() => {
    store.dispatch.mockReset();
    itemsSignal.set([
      {
        itemId: 'item-1',
        name: 'Milk',
        barcode: 'B001',
        description: null,
        uom: 'ltr',
        isActive: true,
        preferredSupplierId: null,
      },
    ]);
    submittingSignal.set(false);
    loadingItemsSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);

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
});
