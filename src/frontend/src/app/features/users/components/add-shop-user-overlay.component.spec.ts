import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { AddShopUserOverlayComponent } from './add-shop-user-overlay.component';
import { UsersActions } from '../state/users.actions';
import { selectUsersErrorMessage, selectUsersSubmitting } from '../state/users.selectors';

describe('AddShopUserOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');

  const authService = {
    session: signal({
      accessToken: '',
      refreshToken: '',
      accessTokenExpiresAt: '',
      refreshTokenExpiresAt: '',
      rememberMe: true,
      user: {
        id: 'u1',
        email: 'owner@test.com',
        phoneNumber: '+15550001111',
        firstName: 'Owner',
        lastName: 'User',
      },
      activeShopId: 's1',
      shops: [
        { shopId: 's1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
        { shopId: 's2', shopName: 'Outlet', role: 'Owner', isDefault: false, lastUsedAt: null },
      ],
    }),
  };

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectUsersSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectUsersErrorMessage) {
        return errorSignal;
      }

      return signal(undefined);
    }),
  };

  function setup(): AddShopUserOverlayComponent {
    TestBed.configureTestingModule({
      imports: [
        AddShopUserOverlayComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: Store, useValue: store },
        { provide: AuthService, useValue: authService },
      ],
    });

    const fixture = TestBed.createComponent(AddShopUserOverlayComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    errorSignal.set('');
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('collects only an address and a role, never credentials', () => {
    const component = setup();

    expect(Object.keys(component.form.controls).sort()).toEqual(['email', 'role']);
  });

  it('invites the address for the shop the owner is working in', () => {
    const component = setup();

    component.form.controls.email.setValue('Invited@Test.com');
    component.form.controls.role.setValue('Staff');
    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.inviteMemberRequested({
        shopId: 's1',
        payload: { email: 'invited@test.com', role: 'Staff' },
      }),
    );
  });

  it('does not invite an invalid address', () => {
    const component = setup();

    component.form.controls.email.setValue('not-an-email');
    component.onSubmit();

    expect(component.form.controls.email.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.inviteMemberRequested.type }),
    );
  });

  it('does not invite while an invitation is already in flight', () => {
    const component = setup();
    component.form.controls.email.setValue('invited@test.com');
    isSubmittingSignal.set(true);
    dispatch.mockReset();

    component.onSubmit();

    expect(dispatch).not.toHaveBeenCalled();
  });
});
