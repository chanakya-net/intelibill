import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { ShopsActions } from '../../features/shops/state/shops.actions';
import { selectShops } from '../../features/shops/state/shops.selectors';
import { ShellComponent } from './shell.component';

describe('ShellComponent', () => {
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

  const store = {
    dispatch: vi.fn(),
    selectSignal: vi.fn((selector: unknown) => {
      if (selector === selectShops) {
        return shopsSignal;
      }

      return signal(false);
    }),
  };

  const shopsSignal = signal([
    { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
  ]);

  function setup(): ShellComponent {
    TestBed.configureTestingModule({
      imports: [ShellComponent, RouterTestingModule.withRoutes([])],
      providers: [
        { provide: AuthService, useValue: authService },
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
    store.dispatch.mockReset();
    store.selectSignal.mockClear();
    authService.needsShopSetup.set(false);
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

  it('dispatches load shops when shell initializes', () => {
    setup();

    expect(store.dispatch).toHaveBeenCalledWith(ShopsActions.loadShopsRequested());
  });

  it('shows initials from first and last name when profile image is unavailable', () => {
    const component = setup();

    expect(component.profileInitials()).toBe('TU');
  });

  it('shows set default store action only when user has more than one shop', () => {
    const component = setup();
    expect(component.shouldShowSetDefaultStoreAction()).toBe(false);

    shopsSignal.set([
        { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
        { shopId: 'shop-2', shopName: 'Branch', role: 'Manager', isDefault: false, lastUsedAt: null },
      ]);

    expect(component.shouldShowSetDefaultStoreAction()).toBe(true);
  });

  it('shows manage shop action when user has at least one shop', () => {
    const component = setup();

    expect(component.shouldShowManageShopAction()).toBe(true);

    shopsSignal.set([]);

    expect(component.shouldShowManageShopAction()).toBe(false);
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
});
