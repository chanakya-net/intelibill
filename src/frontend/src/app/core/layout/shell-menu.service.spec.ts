import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { MenuItem } from 'primeng/api';
import { beforeEach, describe, expect, it } from 'vitest';

import { LocalizationService } from '../i18n/localization.service';
import { ShopPermissionsService } from './shop-permissions.service';
import { ShellMenuService } from './shell-menu.service';

describe('ShellMenuService', () => {
  const roleSignal = signal('Owner');
  const currentLanguage = signal('en');

  const permissionsService = {
    isOwnerOfActiveShop: computed(() => ['owner'].includes(roleSignal().toLowerCase())),
    isOwnerOrManagerOfActiveShop: computed(() =>
      ['owner', 'manager'].includes(roleSignal().toLowerCase()),
    ),
    canManageSuppliers: computed(() => roleSignal().toLowerCase() === 'owner'),
    canManageCustomers: computed(() => ['owner', 'manager'].includes(roleSignal().toLowerCase())),
    canManageSales: computed(() => true),
    canManageExpenses: computed(() => ['owner', 'manager'].includes(roleSignal().toLowerCase())),
    canManageDiscounts: computed(() => ['owner', 'manager'].includes(roleSignal().toLowerCase())),
    canManageServices: computed(() => ['owner', 'manager'].includes(roleSignal().toLowerCase())),
    canManageBankAccounts: computed(() => roleSignal().toLowerCase() === 'owner'),
    canViewInventory: computed(() => true),
  } as ShopPermissionsService;

  const localizationService = {
    currentLanguage,
    translate: (key: string) => `i18n:${key}`,
  };

  beforeEach(() => {
    roleSignal.set('Owner');
    currentLanguage.set('en');
    TestBed.resetTestingModule();
  });

  function createService(): ShellMenuService {
    TestBed.configureTestingModule({
      imports: [RouterTestingModule.withRoutes([])],
      providers: [
        ShellMenuService,
        { provide: ShopPermissionsService, useValue: permissionsService },
        { provide: LocalizationService, useValue: localizationService },
      ],
    });

    return TestBed.inject(ShellMenuService);
  }

  function hasLabel(menuItems: MenuItem[], label: string): boolean {
    return menuItems.some((item) => item.label === label);
  }

  it('builds owner main and inventory menus', () => {
    const service = createService();
    roleSignal.set('Owner');

    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.dashboard')).toBe(true);
    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageInventory')).toBe(true);
    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageSuppliers')).toBe(true);
    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageCustomers')).toBe(true);
    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageSales')).toBe(true);
    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageExpenses')).toBe(true);

    const inventoryItems = service.inventoryMenuItems();
    expect(hasLabel(inventoryItems, 'i18n:shell.addNewProduct')).toBe(true);
    expect(hasLabel(inventoryItems, 'i18n:shell.manageServices')).toBe(true);
    expect(hasLabel(inventoryItems, 'i18n:shell.batchInventoryInbound')).toBe(true);
    expect(hasLabel(inventoryItems, 'i18n:shell.inventoryBatchesOverview')).toBe(true);
    expect(hasLabel(inventoryItems, 'i18n:shell.inventoryAdjustments')).toBe(true);
  });

  it('hides owner-only sections for staff while keeping adjustments', () => {
    const service = createService();
    roleSignal.set('Staff');

    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageSuppliers')).toBe(false);
    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageCustomers')).toBe(false);
    expect(hasLabel(service.mainMenuItems(), 'i18n:shell.manageExpenses')).toBe(false);

    const inventoryItems = service.inventoryMenuItems();
    expect(hasLabel(inventoryItems, 'i18n:shell.addNewProduct')).toBe(false);
    expect(hasLabel(inventoryItems, 'i18n:shell.manageServices')).toBe(false);
    expect(hasLabel(inventoryItems, 'i18n:shell.batchInventoryInbound')).toBe(false);
    expect(hasLabel(inventoryItems, 'i18n:shell.inventoryBatchesOverview')).toBe(false);
    expect(hasLabel(inventoryItems, 'i18n:shell.inventoryAdjustments')).toBe(true);
  });
});
