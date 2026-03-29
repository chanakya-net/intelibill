import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { ShopDetails } from '../../features/shops/services/shop.service';
import { ShopsActions } from '../../features/shops/state/shops.actions';
import { selectShopDetailsEntities, selectShops, selectShopsSubmitting } from '../../features/shops/state/shops.selectors';
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

      if (selector === selectShopDetailsEntities) {
        return shopDetailsByIdSignal;
      }

      if (selector === selectShopsSubmitting) {
        return shopsSubmittingSignal;
      }

      return signal(false);
    }),
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
    },
  });

  const shopsSubmittingSignal = signal(false);

  function createFixture() {
    TestBed.configureTestingModule({
      imports: [ShellComponent, RouterTestingModule.withRoutes([])],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Store, useValue: store },
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
    shopDetailsByIdSignal.set({
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
      },
    });
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

  it('dispatches load shops when shell initializes', () => {
    setup();

    expect(store.dispatch).toHaveBeenCalledWith(ShopsActions.loadShopsRequested());
  });

  it('shows initials from first and last name when profile image is unavailable', () => {
    const component = setup();

    expect(component.profileInitials()).toBe('TU');
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

  it('shows active shop name with pincode beside app title and updates when active shop changes', () => {
    const fixture = createFixture();
    const activeShopTrigger = fixture.nativeElement.querySelector('.active-shop-trigger') as HTMLElement;

    expect(activeShopTrigger.textContent?.replace(/\s+/g, ' ').trim()).toContain('Main - 560001');

    shopsSignal.set([
      { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: false, lastUsedAt: null },
      { shopId: 'shop-2', shopName: 'Branch', role: 'Manager', isDefault: true, lastUsedAt: null },
    ]);
    shopDetailsByIdSignal.set({
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
      },
      'shop-2': {
        shopId: 'shop-2',
        name: 'Branch',
        address: 'Address',
        city: 'City',
        state: 'State',
        pincode: '110001',
        contactPerson: null,
        mobileNumber: null,
        gstNumber: null,
      },
    });
    fixture.detectChanges();

    expect(activeShopTrigger.textContent?.replace(/\s+/g, ' ').trim()).toContain('Branch - 110001');
  });

  it('toggles active shop menu from the header', () => {
    const component = setup();

    expect(component.isShopMenuOpen()).toBe(false);

    component.onToggleShopMenu();
    expect(component.isShopMenuOpen()).toBe(true);

    component.onToggleShopMenu();
    expect(component.isShopMenuOpen()).toBe(false);
  });

  it('switches active shop through NgRx when selecting a different shop', () => {
    const component = setup();
    shopsSignal.set([
      { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
      { shopId: 'shop-2', shopName: 'Branch', role: 'Manager', isDefault: false, lastUsedAt: null },
    ]);

    component.onToggleShopMenu();
    component.onSelectShop('shop-2');

    expect(store.dispatch).toHaveBeenCalledWith(ShopsActions.clearError());
    expect(store.dispatch).toHaveBeenCalledWith(ShopsActions.clearMutationStatus());
    expect(store.dispatch).toHaveBeenCalledWith(ShopsActions.setDefaultShopRequested({ shopId: 'shop-2' }));
    expect(component.isShopMenuOpen()).toBe(false);
  });

  it('does not dispatch switch action when selecting currently active shop', () => {
    const component = setup();
    store.dispatch.mockClear();

    component.onToggleShopMenu();
    component.onSelectShop('shop-1');

    expect(store.dispatch).not.toHaveBeenCalled();
    expect(component.isShopMenuOpen()).toBe(false);
  });

  it('closes shop menu when clicking outside', () => {
    const fixture = createFixture();
    const component = fixture.componentInstance;

    component.onToggleShopMenu();
    expect(component.isShopMenuOpen()).toBe(true);

    component.onDocumentClick({ target: document.body } as unknown as MouseEvent);

    expect(component.isShopMenuOpen()).toBe(false);
  });

  it('closes profile menu when clicking outside', () => {
    const fixture = createFixture();
    const component = fixture.componentInstance;

    component.onToggleProfileMenu();
    expect(component.isProfileMenuOpen()).toBe(true);

    component.onDocumentClick({ target: document.body } as unknown as MouseEvent);

    expect(component.isProfileMenuOpen()).toBe(false);
  });

  it('closes both menus when pointerdown happens outside', () => {
    const fixture = createFixture();
    const component = fixture.componentInstance;

    component.onToggleShopMenu();
    component.onToggleProfileMenu();

    component.onDocumentPointerDown({ target: document.body, composedPath: () => [document.body] } as unknown as PointerEvent);

    expect(component.isShopMenuOpen()).toBe(false);
    expect(component.isProfileMenuOpen()).toBe(false);
  });
});
