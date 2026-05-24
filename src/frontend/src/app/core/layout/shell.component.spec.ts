import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { beforeAll, beforeEach, afterEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { LocalizationService } from '../i18n/localization.service';
import { ShopDetails } from '../../features/shops/services/shop.service';
import { selectShopDetailsEntities, selectShops, selectShopsSubmitting } from '../../features/shops/state/shops.selectors';
import { UsersActions } from '../../features/users/state/users.actions';
import { SupportedLanguage } from '../i18n/language.constants';
import { ShellComponent } from './shell.component';

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
  const language = signal<SupportedLanguage>('en-IN');
  const localizationService = {
    currentLanguage: language.asReadonly(),
    translate: vi.fn((key: string) => key),
    setLanguage: vi.fn(async (value: SupportedLanguage) => {
      language.set(value);
    }),
  };

  const sessionSignal = signal({
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

      return signal(false);
    }),
  };

  function setup(): ShellComponent {
    TestBed.configureTestingModule({
      imports: [ShellComponent, RouterTestingModule.withRoutes([]), TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: LocalizationService, useValue: localizationService },
        { provide: Store, useValue: store },
      ],
    });

    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    authService.signOutAndRedirect.mockReset();
    authService.signOutAndRedirect.mockReturnValue(of(void 0));
    authService.needsShopSetup.set(false);
    store.dispatch.mockReset();
    store.selectSignal.mockClear();
    localizationService.setLanguage.mockClear();
    language.set('en-IN');
    sessionSignal.set({
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
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });
    shopsSignal.set([
      { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
    ]);
    shopsSubmittingSignal.set(false);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('signs out when create-shop overlay close is requested', () => {
    authService.needsShopSetup.set(true);
    const component = setup();

    component.onCreateShopOverlayClose();

    expect(authService.signOutAndRedirect).toHaveBeenCalledTimes(1);
    expect(component.isSigningOut()).toBe(false);
  });

  it('opens update profile overlay from profile actions', () => {
    const component = setup();

    component.onOpenUpdateProfile();

    expect(component.showUpdateProfileOverlay()).toBe(true);
    expect(component.isProfileMenuOpen()).toBe(false);
  });

  it('opens change password overlay from profile actions', () => {
    const component = setup();

    component.onOpenChangePassword();

    expect(component.showChangePasswordOverlay()).toBe(true);
    expect(component.isProfileMenuOpen()).toBe(false);
  });

  it('opens add shop overlay from profile actions', () => {
    const component = setup();

    component.onOpenAddShop();

    expect(component.showCreateShopOverlay()).toBe(true);
    expect(component.isProfileMenuOpen()).toBe(false);
  });

  it('opens manage shop overlay from profile actions', () => {
    const component = setup();

    component.onOpenManageShop();

    expect(component.showManageShopOverlay()).toBe(true);
    expect(component.isProfileMenuOpen()).toBe(false);
  });

  it('keeps create-shop overlay open until explicitly closed after setup becomes optional', () => {
    authService.needsShopSetup.set(true);
    const component = setup();

    expect(component.showCreateShopOverlay()).toBe(true);

    authService.needsShopSetup.set(false);

    expect(component.showCreateShopOverlay()).toBe(true);

    component.onCreateShopOverlayClose();

    expect(component.showCreateShopOverlay()).toBe(false);
    expect(authService.signOutAndRedirect).not.toHaveBeenCalled();
  });

  it('updates the profile language through the shell action', () => {
    const component = setup();
    const nextLanguage: SupportedLanguage = 'hi-IN';

    component.onLanguageSelected(nextLanguage);

    expect(localizationService.setLanguage).toHaveBeenCalledWith(nextLanguage);
    expect(store.dispatch).toHaveBeenCalledWith(UsersActions.clearError());
    expect(store.dispatch).toHaveBeenCalledWith(
      UsersActions.updateProfileRequested({
        payload: {
          email: 'user@example.com',
          phoneNumber: null,
          firstName: 'Test',
          lastName: 'User',
          language: nextLanguage,
        },
      }),
    );
  });
});
