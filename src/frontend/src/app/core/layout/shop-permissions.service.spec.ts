import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { ShopPermissionsService } from './shop-permissions.service';

describe('ShopPermissionsService', () => {
  const sessionSignal = signal({
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
  } as never);

  const authService = {
    session: sessionSignal,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    sessionSignal.set({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    } as never);
  });

  function createService(): ShopPermissionsService {
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: authService }],
    });

    return TestBed.inject(ShopPermissionsService);
  }

  it.each([
    ['owner', true, true, true, true, true, true, true, true, true],
    ['manager', false, true, false, true, true, true, true, false, true],
    ['staff', false, false, false, false, false, false, false, false, true],
  ] satisfies Array<[string, boolean, boolean, boolean, boolean, boolean, boolean, boolean, boolean, boolean]>)(
    'computes permissions for %s role',
    (
      role,
      isOwner,
      isOwnerManager,
      canManageSuppliers,
      canManageCustomers,
      canManageExpenses,
      canManageDiscounts,
      canManageServices,
      canManageBankAccounts,
      canViewInventory,
    ) => {
      const service = createService();
      sessionSignal.set({
        activeShopId: 'shop-1',
        shops: [{ shopId: 'shop-1', shopName: 'Main', role, isDefault: true, lastUsedAt: null }],
      } as never);

      expect(service.isOwnerOfActiveShop()).toBe(isOwner);
      expect(service.isOwnerOrManagerOfActiveShop()).toBe(isOwnerManager);
      expect(service.canManageSuppliers()).toBe(canManageSuppliers);
      expect(service.canManageCustomers()).toBe(canManageCustomers);
      expect(service.canManageSales()).toBe(true);
      expect(service.canManageExpenses()).toBe(canManageExpenses);
      expect(service.canManageDiscounts()).toBe(canManageDiscounts);
      expect(service.canManageServices()).toBe(canManageServices);
      expect(service.canManageBankAccounts()).toBe(canManageBankAccounts);
      expect(service.canViewInventory()).toBe(canViewInventory);
    },
  );

  it('returns false for all permissions when no active shop exists', () => {
    const service = createService();
    sessionSignal.set({
      activeShopId: null,
      shops: [],
    } as never);

    expect(service.isOwnerOfActiveShop()).toBe(false);
    expect(service.isOwnerOrManagerOfActiveShop()).toBe(false);
    expect(service.canManageSuppliers()).toBe(false);
    expect(service.canManageCustomers()).toBe(false);
    expect(service.canManageSales()).toBe(false);
    expect(service.canManageExpenses()).toBe(false);
    expect(service.canManageDiscounts()).toBe(false);
    expect(service.canManageServices()).toBe(false);
    expect(service.canManageBankAccounts()).toBe(false);
    expect(service.canViewInventory()).toBe(false);
  });

  it('falls back to the default shop when activeShopId is stale', () => {
    const service = createService();
    sessionSignal.set({
      activeShopId: 'missing-shop',
      shops: [
        { shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null },
      ],
    } as never);

    expect(service.activeShopId()).toBe('shop-1');
    expect(service.canManageServices()).toBe(true);
  });
});
