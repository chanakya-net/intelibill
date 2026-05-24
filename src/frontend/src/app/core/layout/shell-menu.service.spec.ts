import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { describe, expect, it, vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { AuthSession } from '../auth/auth.models';
import { LocalizationService } from '../i18n/localization.service';
import { ShopPermissionsService } from './shop-permissions.service';
import { ShellMenuService } from './shell-menu.service';

function buildSession(role: string): AuthSession {
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

describe('ShellMenuService', () => {
  const session = signal<AuthSession | null>(null);
  const language = signal('en');

  const router = {
    navigate: vi.fn<Router['navigate']>().mockResolvedValue(true),
  };

  const localizationService = {
    currentLanguage: language.asReadonly(),
    translate: vi.fn((key: string) => `${language()}:${key}`),
  };

  const authService = {
    session,
  };

  function setup(): ShellMenuService {
    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: router },
        { provide: LocalizationService, useValue: localizationService },
        { provide: AuthService, useValue: authService },
        ShopPermissionsService,
      ],
    });

    return TestBed.inject(ShellMenuService);
  }

  beforeEach(() => {
    session.set(null);
    language.set('en');
    router.navigate.mockReset();
    localizationService.translate.mockClear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('builds the main menu from the active shop permissions', () => {
    const service = setup();
    session.set(buildSession('Owner'));

    const items = service.mainMenuItems();

    expect(items.map((item) => item.label)).toEqual([
      'en:shell.dashboard',
      'en:shell.manageInventory',
      'en:shell.manageSuppliers',
      'en:shell.manageCustomers',
      'en:shell.manageSales',
      'en:shell.manageExpenses',
    ]);
    expect(items[1]?.items?.map((item) => item.label)).toEqual([
      'en:shell.addNewProduct',
      'en:shell.batchInventoryInbound',
      'en:shell.inventoryBatchesOverview',
      'en:shell.inventoryAdjustments',
    ]);
    expect(items[4]?.items?.map((item) => item.label)).toEqual([
      'en:shell.newSale',
      'en:shell.salesHistory',
      'en:shell.profitLossReport',
    ]);
  });

  it('reacts to language and permission changes', () => {
    const service = setup();
    session.set(buildSession('Staff'));

    expect(service.mainMenuItems().map((item) => item.label)).toEqual([
      'en:shell.dashboard',
      'en:shell.manageInventory',
      'en:shell.manageSales',
    ]);
    expect(service.inventoryMenuItems().map((item) => item.label)).toEqual([
      'en:shell.inventoryAdjustments',
    ]);

    language.set('fr');

    expect(service.mainMenuItems()[0]?.label).toBe('fr:shell.dashboard');
    expect(service.inventoryMenuItems()[0]?.label).toBe('fr:shell.inventoryAdjustments');
  });

  it('navigates from menu commands', async () => {
    const service = setup();
    session.set(buildSession('Manager'));

    service.mainMenuItems()[0]?.command?.({ originalEvent: new Event('click') } as never);

    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
  });
});
