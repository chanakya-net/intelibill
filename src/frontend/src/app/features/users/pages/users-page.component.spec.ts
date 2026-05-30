import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopUser } from '../services/user-account.service';
import { UsersActions } from '../state/users.actions';
import {
  selectShopUsers,
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersLoadingShopUsers,
} from '../state/users.selectors';
import { UsersPageComponent } from './users-page.component';

describe('UsersPageComponent', () => {
  const shopUsersSignal = signal<ShopUser[]>([
    {
      userId: 'u1',
      firstName: 'Owner',
      lastName: 'User',
      email: 'owner@test.com',
      phoneNumber: '+15551234567',
      role: 'Owner',
      isLoginEnabled: true,
      shopIds: ['shop-1'],
    },
  ]);
  const loadingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'update-profile' | 'change-password' | 'add-shop-user' | 'edit-shop-user' | null>(null);
  const lastMutationSucceededSignal = signal(false);

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

      if (selector === selectUsersLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectUsersLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      return signal(null);
    }),
  };

  const defaultSession = {
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
  };
  const sessionSignal = signal(defaultSession);

  const authService = {
    session: sessionSignal,
  };

  beforeEach(() => {
    store.dispatch.mockReset();
    shopUsersSignal.set([
      {
        userId: 'u1',
        firstName: 'Owner',
        lastName: 'User',
        email: 'owner@test.com',
        phoneNumber: '+15551234567',
        role: 'Owner',
        isLoginEnabled: true,
        shopIds: ['shop-1'],
      },
    ]);
    loadingSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
    sessionSignal.set(defaultSession);
    TestBed.configureTestingModule({
      imports: [UsersPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
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

  it('normalizes staff role label for table display', () => {
    const fixture = TestBed.createComponent(UsersPageComponent);
    const component = fixture.componentInstance;

    expect(component.getRoleLabel('SalesPerson')).toBe('users.staff');
    expect(component.getRoleLabel('Staff')).toBe('users.staff');
  });

  it('closes add overlay on successful add mutation', () => {
    const fixture = TestBed.createComponent(UsersPageComponent);
    const component = fixture.componentInstance;

    component.onOpenAddUser();
    expect(component.showAddUserOverlay()).toBe(true);

    lastMutationTypeSignal.set('add-shop-user');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.showAddUserOverlay()).toBe(false);
    expect(store.dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
  });

  it('closes edit overlay on successful edit mutation', () => {
    const fixture = TestBed.createComponent(UsersPageComponent);
    const component = fixture.componentInstance;
    const targetUser = { ...shopUsersSignal()[0], role: 'Manager' };

    component.onOpenEditUser(targetUser);
    expect(component.showEditUserOverlay()).toBe(true);

    lastMutationTypeSignal.set('edit-shop-user');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.showEditUserOverlay()).toBe(false);
    expect(component.editingUser()).toBeNull();
    expect(store.dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
  });

  it('returns a mutable table copy so sorting does not mutate store state', () => {
    const fixture = TestBed.createComponent(UsersPageComponent);
    const component = fixture.componentInstance;
    const frozenUsers = Object.freeze([
      Object.freeze({
        userId: 'u2',
        firstName: 'Zara',
        lastName: 'User',
        email: 'zara@test.com',
        phoneNumber: '+15557654321',
        role: 'Manager',
        isLoginEnabled: true,
        shopIds: Object.freeze(['shop-2']),
      }),
      Object.freeze({
        userId: 'u1',
        firstName: 'Ayaan',
        lastName: 'User',
        email: 'ayaan@test.com',
        phoneNumber: '+15551234567',
        role: 'Owner',
        isLoginEnabled: true,
        shopIds: Object.freeze(['shop-1']),
      }),
    ]);

    shopUsersSignal.set(frozenUsers as unknown as ShopUser[]);

    const tableUsers = component.tableUsers();

    expect(tableUsers).not.toBe(shopUsersSignal());
    expect(() => tableUsers.sort((left, right) => left.firstName.localeCompare(right.firstName))).not.toThrow();
    expect(shopUsersSignal()[0].firstName).toBe('Zara');
    expect(shopUsersSignal()[1].firstName).toBe('Ayaan');
  });

  it('updates table users when shop users state changes', () => {
    const fixture = TestBed.createComponent(UsersPageComponent);
    const component = fixture.componentInstance;

    expect(component.tableUsers().map((user) => user.userId)).toEqual(['u1']);

    shopUsersSignal.set([
      {
        userId: 'u3',
        firstName: 'New',
        lastName: 'Shop',
        email: 'newshop@test.com',
        phoneNumber: '+15559876543',
        role: 'Manager',
        isLoginEnabled: true,
        shopIds: ['shop-2'],
      },
    ]);
    fixture.detectChanges();

    expect(component.tableUsers().map((user) => user.userId)).toEqual(['u3']);
  });
});
