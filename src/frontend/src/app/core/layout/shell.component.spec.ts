import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { beforeAll, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { ShopDetails } from '../../features/shops/services/shop.service';
import { ShopsActions } from '../../features/shops/state/shops.actions';
import { selectShopDetailsEntities, selectShops, selectShopsSubmitting } from '../../features/shops/state/shops.selectors';
import { UsersActions } from '../../features/users/state/users.actions';
import { LocalizationService } from '../i18n/localization.service';
import { DEFAULT_LANGUAGE, SupportedLanguage } from '../i18n/language.constants';
import { ShellComponent } from './shell.component';
import { selectSidebarCollapsed, selectSidebarPinned } from '../state/app-shell.selectors';

beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
});

describe('ShellComponent', () => {
  const sessionSignal = signal({
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
    rememberMe: true,
    user: {
      id: 'user-1',
      email: 'test@example.com',
      phoneNumber: null,
      firstName: 'Test',
      lastName: 'User',
      language: 'en-IN',
    },
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
  });

  const authService = {
    needsShopSetup: signal(false),
    session: sessionSignal,
    signOutAndRedirect: vi.fn<AuthService['signOutAndRedirect']>(),
  };

  const shopsSignal = signal([
    { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
  ]);

  const shopDetailsByIdSignal = signal<Record<string, ShopDetails>>({
    'shop-1': {
      shopId: 'shop-1',
      name: 'Main',
      address: 'Address',
      city: 'City',
      state: 'State',
      pincode: '560001',
      contactPerson: null,
      mobileNumber: null,
      gstNumber: null,
      bankName: null,
      bankAccountNumber: null,
      bankAccountType: null,
      ifscCode: null,
      accountHolderName: null,
    },
  });

  const shopsSubmittingSignal = signal(false);
  const sidebarCollapsedSignal = signal(false);
  const sidebarPinnedSignal = signal(false);
  const currentLanguage = signal(DEFAULT_LANGUAGE);

  const localizationService = {
    currentLanguage,
    translate: (value: string) => value,
    setLanguage: vi.fn((language: SupportedLanguage) => {
      currentLanguage.set(language);
      return Promise.resolve();
    }),
  };

  const store = {
    dispatch: vi.fn(),
    selectSignal: vi.fn((selector: unknown) => {
      if (selector === selectShops) {
        return shopsSignal;
      }

      if (selector === selectShopDetailsEntities) {
        return shopDetailsByIdSignal;
      }

      if (selector === selectShopsSubmitting) {
        return shopsSubmittingSignal;
      }

      if (selector === selectSidebarCollapsed) {
        return sidebarCollapsedSignal;
      }

      if (selector === selectSidebarPinned) {
        return sidebarPinnedSignal;
      }

      return signal(false);
    }),
  };

  function createFixture() {
    TestBed.configureTestingModule({
      imports: [ShellComponent, RouterTestingModule.withRoutes([]), TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Store, useValue: store },
        { provide: LocalizationService, useValue: localizationService },
      ],
    });

    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    return fixture;
  }

  function setup(): ShellComponent {
    return createFixture().componentInstance;
  }

  beforeEach(() => {
    const currentSession = sessionSignal();
    sessionSignal.set({
      ...currentSession,
      activeShopId: 'shop-1',
      shops: [
        {
          shopId: 'shop-1',
          shopName: 'Main',
          role: 'Owner',
          isDefault: true,
          lastUsedAt: null,
        },
        {
          shopId: 'shop-2',
          shopName: 'Secondary',
          role: 'Owner',
          isDefault: false,
          lastUsedAt: null,
        },
      ],
    });
    shopsSignal.set(sessionSignal().shops);
    currentLanguage.set(DEFAULT_LANGUAGE);
    authService.signOutAndRedirect.mockReset();
    authService.signOutAndRedirect.mockReturnValue(of(void 0));
    store.dispatch.mockReset();
    authService.needsShopSetup.set(false);
    store.selectSignal.mockClear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('opens add shop overlay and closes it after dismissal', () => {
    const component = setup();

    component.onOpenAddShop();
    expect(component.showCreateShopOverlayAuto() || component.showCreateShopOverlayManual()).toBe(true);

    component.onCreateShopOverlayClose();
    expect(component.showCreateShopOverlayAuto() || component.showCreateShopOverlayManual()).toBe(false);
  });

  it('closes create-shop overlay and signs out when shop setup is required', () => {
    const component = setup();
    authService.needsShopSetup.set(true);

    component.onCreateShopOverlayClose();

    expect(component.showCreateShopOverlayAuto() || component.showCreateShopOverlayManual()).toBe(false);
    expect(authService.signOutAndRedirect).toHaveBeenCalledTimes(1);
    expect(component.isSigningOut()).toBe(false);
  });

  it('keeps create-shop overlay open and does not sign out when setup is optional', () => {
    const component = setup();
    authService.needsShopSetup.set(false);

    component.onOpenAddShop();
    expect(component.showCreateShopOverlayAuto() || component.showCreateShopOverlayManual()).toBe(true);

    authService.needsShopSetup.set(false);
    component.onCreateShopOverlayClose();
    expect(component.showCreateShopOverlayAuto() || component.showCreateShopOverlayManual()).toBe(false);
    expect(authService.signOutAndRedirect).not.toHaveBeenCalled();
  });

  it('opens and closes profile overlays from profile actions', () => {
    const component = setup();

    component.onOpenUpdateProfile();
    expect(component.showUpdateProfileOverlay()).toBe(true);

    component.onProfileOverlayClose();
    expect(component.showUpdateProfileOverlay()).toBe(false);

    component.onOpenChangePassword();
    expect(component.showChangePasswordOverlay()).toBe(true);

    component.onProfileOverlayClose();
    expect(component.showChangePasswordOverlay()).toBe(false);
  });

  it('opens and closes manage shop overlay', () => {
    const component = setup();

    component.onOpenManageShop();

    expect(component.showManageShopOverlay()).toBe(true);

    component.onManageShopOverlayClose();
    expect(component.showManageShopOverlay()).toBe(false);
  });

  it('switches language and updates user profile language', () => {
    const component = setup();

    component.onLanguageSelected('hi-IN');

    expect(store.dispatch).toHaveBeenCalledWith(
      UsersActions.updateProfileRequested({
        payload: expect.objectContaining({ language: 'hi-IN' }),
      }),
    );
  });

  it('does not update language when selected language is already active', () => {
    const component = setup();
    store.dispatch.mockClear();

    component.onLanguageSelected('en-IN');

    expect(store.dispatch).not.toHaveBeenCalled();
    expect(authService.signOutAndRedirect).not.toHaveBeenCalled();
  });

  it('collapses the sidebar after a menu item requests auto-hide', () => {
    const component = setup();
    store.dispatch.mockClear();
    sidebarCollapsedSignal.set(false);
    sidebarPinnedSignal.set(false);

    component.onAutoHideSidebar();

    expect(store.dispatch).toHaveBeenCalledWith(
      expect.objectContaining({ collapsed: true, type: '[App Shell] Set Sidebar Collapsed' }),
    );
  });

  it('does not auto-hide the sidebar when it is pinned', () => {
    const component = setup();
    store.dispatch.mockClear();
    sidebarCollapsedSignal.set(false);
    sidebarPinnedSignal.set(true);

    component.onAutoHideSidebar();

    expect(store.dispatch).not.toHaveBeenCalled();
  });

  it('toggles sidebar pin state', () => {
    const component = setup();
    store.dispatch.mockClear();

    component.onToggleSidebarPin();

    expect(store.dispatch).toHaveBeenCalledWith(
      expect.objectContaining({ type: '[App Shell] Toggle Sidebar Pinned' }),
    );
  });

  it.each(['Owner', 'Manager', 'Staff'])(
    'lets an assigned %s open the shop menu and request a shop switch',
    (role) => {
      const currentSession = sessionSignal();
      const shops = currentSession.shops.map((shop) => ({ ...shop, role }));
      sessionSignal.set({ ...currentSession, shops });
      shopsSignal.set(shops);
      const component = setup();
      store.dispatch.mockClear();

      component.onToggleShopMenu();
      expect(component.isShopMenuOpen()).toBe(true);

      component.onSelectShop('shop-2');
      expect(store.dispatch).toHaveBeenCalledWith(
        ShopsActions.setDefaultShopRequested({ shopId: 'shop-2' }),
      );
      expect(component.isShopMenuOpen()).toBe(false);
    },
  );
});
