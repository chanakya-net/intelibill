import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { Supplier } from '../services/supplier.service';
import { SuppliersActions } from '../state/suppliers.actions';
import {
  selectSuppliers,
  selectSuppliersErrorMessage,
  selectSuppliersLastMutationSucceeded,
  selectSuppliersLastMutationType,
  selectSuppliersLoading,
} from '../state/suppliers.selectors';
import { SuppliersPageComponent } from './suppliers-page.component';

describe('SuppliersPageComponent', () => {
  const suppliersSignal = signal<Supplier[]>([
    {
      supplierId: 's1',
      name: 'Fresh Foods',
      contactPersonName: 'Ramesh',
      contactPersonPhone: '+919999999999',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pin: '560001',
      isActive: true,
      isPreferred: true,
    },
  ]);
  const loadingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'add-supplier' | 'edit-supplier' | null>(null);
  const lastMutationSucceededSignal = signal(false);

  const store = {
    dispatch: vi.fn(),
    selectSignal: vi.fn((selector: unknown) => {
      if (selector === selectSuppliers) {
        return suppliersSignal;
      }

      if (selector === selectSuppliersLoading) {
        return loadingSignal;
      }

      if (selector === selectSuppliersErrorMessage) {
        return errorSignal;
      }

      if (selector === selectSuppliersLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectSuppliersLastMutationSucceeded) {
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
    suppliersSignal.set([
      {
        supplierId: 's1',
        name: 'Fresh Foods',
        contactPersonName: 'Ramesh',
        contactPersonPhone: '+919999999999',
        address: '42 MG Road',
        city: 'Bengaluru',
        state: 'Karnataka',
        pin: '560001',
        isActive: true,
        isPreferred: true,
      },
    ]);
    loadingSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);

    TestBed.configureTestingModule({
      imports: [SuppliersPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: Store, useValue: store },
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads suppliers on init', () => {
    TestBed.createComponent(SuppliersPageComponent);

    expect(store.dispatch).toHaveBeenCalledWith(SuppliersActions.loadSuppliersRequested());
  });

  it('closes add overlay on successful add mutation', () => {
    const fixture = TestBed.createComponent(SuppliersPageComponent);
    const component = fixture.componentInstance;

    component.onOpenAddSupplier();
    expect(component.showAddSupplierOverlay()).toBe(true);

    lastMutationTypeSignal.set('add-supplier');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.showAddSupplierOverlay()).toBe(false);
    expect(store.dispatch).toHaveBeenCalledWith(SuppliersActions.clearMutationStatus());
  });

  it('allows editing only for owner role', () => {
    const fixture = TestBed.createComponent(SuppliersPageComponent);
    const component = fixture.componentInstance;

    expect(component.canManageSuppliers()).toBe(true);

    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null }],
    });

    expect(component.canManageSuppliers()).toBe(false);
  });
});