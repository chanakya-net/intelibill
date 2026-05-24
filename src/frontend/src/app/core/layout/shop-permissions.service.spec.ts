import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { describe, expect, it } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { AuthSession } from '../auth/auth.models';
import { ShopPermissionsService } from './shop-permissions.service';

function buildSession(role: string | null): AuthSession | null {
  if (!role) {
    return null;
  }

  return {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
    rememberMe: true,
    user: {
      id: 'user-1',
      email: 'user@example.com',
      phoneNumber: null,
      firstName: 'Test',
      lastName: 'User',
    },
    activeShopId: 'shop-1',
    shops: [
      {
        shopId: 'shop-1',
        shopName: 'Main',
        role,
        isDefault: true,
        lastUsedAt: null,
      },
    ],
  };
}

describe('ShopPermissionsService', () => {
  const session = signal<AuthSession | null>(null);

  const authService = {
    session,
  };

  function setup(): ShopPermissionsService {
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: authService }],
    });

    return TestBed.inject(ShopPermissionsService);
  }

  afterEach(() => {
    session.set(null);
    TestBed.resetTestingModule();
  });

  it.each([
    [
      'Owner',
      {
        isOwnerOfActiveShop: true,
        isOwnerOrManagerOfActiveShop: true,
        canManageSuppliers: true,
        canManageCustomers: true,
        canManageSales: true,
        canManageExpenses: true,
        canManageDiscounts: true,
        canManageBankAccounts: true,
        canViewInventory: true,
      },
    ],
    [
      'Manager',
      {
        isOwnerOfActiveShop: false,
        isOwnerOrManagerOfActiveShop: true,
        canManageSuppliers: false,
        canManageCustomers: true,
        canManageSales: true,
        canManageExpenses: true,
        canManageDiscounts: true,
        canManageBankAccounts: false,
        canViewInventory: true,
      },
    ],
    [
      'Staff',
      {
        isOwnerOfActiveShop: false,
        isOwnerOrManagerOfActiveShop: false,
        canManageSuppliers: false,
        canManageCustomers: false,
        canManageSales: true,
        canManageExpenses: false,
        canManageDiscounts: false,
        canManageBankAccounts: false,
        canViewInventory: true,
      },
    ],
  ])('maps %s role permissions from active shop', (role, expected) => {
    const service = setup();
    session.set(buildSession(role as string));

    expect(service.isOwnerOfActiveShop()).toBe(expected.isOwnerOfActiveShop);
    expect(service.isOwnerOrManagerOfActiveShop()).toBe(expected.isOwnerOrManagerOfActiveShop);
    expect(service.canManageSuppliers()).toBe(expected.canManageSuppliers);
    expect(service.canManageCustomers()).toBe(expected.canManageCustomers);
    expect(service.canManageSales()).toBe(expected.canManageSales);
    expect(service.canManageExpenses()).toBe(expected.canManageExpenses);
    expect(service.canManageDiscounts()).toBe(expected.canManageDiscounts);
    expect(service.canManageBankAccounts()).toBe(expected.canManageBankAccounts);
    expect(service.canViewInventory()).toBe(expected.canViewInventory);
  });

  it('returns false for every permission when no session is available', () => {
    const service = setup();

    expect(service.isOwnerOfActiveShop()).toBe(false);
    expect(service.isOwnerOrManagerOfActiveShop()).toBe(false);
    expect(service.canManageSuppliers()).toBe(false);
    expect(service.canManageCustomers()).toBe(false);
    expect(service.canManageSales()).toBe(false);
    expect(service.canManageExpenses()).toBe(false);
    expect(service.canManageDiscounts()).toBe(false);
    expect(service.canManageBankAccounts()).toBe(false);
    expect(service.canViewInventory()).toBe(false);
  });
});
