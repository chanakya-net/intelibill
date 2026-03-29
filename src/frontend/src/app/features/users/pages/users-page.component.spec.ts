import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { UsersActions } from '../state/users.actions';
import { selectShopUsers, selectUsersErrorMessage, selectUsersLoadingShopUsers } from '../state/users.selectors';
import { UsersPageComponent } from './users-page.component';

describe('UsersPageComponent', () => {
  const shopUsersSignal = signal([
    {
      userId: 'u1',
      firstName: 'Owner',
      lastName: 'User',
      email: 'owner@test.com',
      phoneNumber: '+15551234567',
      role: 'Owner',
    },
  ]);
  const loadingSignal = signal(false);
  const errorSignal = signal('');

  const store = {
    dispatch: vi.fn(),
    selectSignal: vi.fn((selector: unknown) => {
      if (selector === selectShopUsers) {
        return shopUsersSignal;
      }

      if (selector === selectUsersLoadingShopUsers) {
        return loadingSignal;
      }

      if (selector === selectUsersErrorMessage) {
        return errorSignal;
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
    TestBed.configureTestingModule({
      imports: [UsersPageComponent],
      providers: [
        { provide: Store, useValue: store },
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads shop users on init', () => {
    TestBed.createComponent(UsersPageComponent);

    expect(store.dispatch).toHaveBeenCalledWith(UsersActions.loadShopUsersRequested());
  });

  it('allows add user only for active owner', () => {
    const fixture = TestBed.createComponent(UsersPageComponent);
    const component = fixture.componentInstance;

    expect(component.canAddUsers()).toBe(true);

    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null }],
    });

    expect(component.canAddUsers()).toBe(false);
  });

  it('normalizes salesperson role label for table display', () => {
    const fixture = TestBed.createComponent(UsersPageComponent);
    const component = fixture.componentInstance;

    expect(component.getRoleLabel('SalesPerson')).toBe('Sales Person');
    expect(component.getRoleLabel('Staff')).toBe('Sales Person');
  });
});
