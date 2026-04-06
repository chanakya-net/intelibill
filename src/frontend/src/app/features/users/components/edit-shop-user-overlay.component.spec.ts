import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { EditShopUserOverlayComponent } from './edit-shop-user-overlay.component';
import { UsersActions } from '../state/users.actions';
import { selectUsersErrorMessage, selectUsersSubmitting } from '../state/users.selectors';

describe('EditShopUserOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');

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

  const authService = {
    session: signal({
      accessToken: '',
      refreshToken: '',
      accessTokenExpiresAt: '',
      refreshTokenExpiresAt: '',
      rememberMe: true,
      user: {
        id: 'u-owner',
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

  function setup(): EditShopUserOverlayComponent {
    TestBed.configureTestingModule({
      imports: [EditShopUserOverlayComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: Store, useValue: store },
        { provide: AuthService, useValue: authService },
      ],
    });

    const fixture = TestBed.createComponent(EditShopUserOverlayComponent);
    fixture.componentInstance.user = {
      userId: 'u-staff',
      firstName: 'Sales',
      lastName: 'User',
      email: 'sales@test.com',
      phoneNumber: '+15551234567',
      role: 'Staff',
      isLoginEnabled: true,
      shopIds: ['s1'],
    };
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

  it('does not submit when phone number is invalid', () => {
    const component = setup();

    component.form.controls.phoneNumber.setValue('123');
    component.onSubmit();

    expect(component.form.controls.phoneNumber.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.editShopUserRequested.type })
    );
  });

  it('dispatches edit user action with trimmed payload', () => {
    const component = setup();

    component.form.controls.email.setValue('  sales.updated@test.com  ');
    component.form.controls.firstName.setValue('  Sales  ');
    component.form.controls.lastName.setValue('  Updated  ');
    component.form.controls.phoneNumber.setValue('+15551234569');
    component.form.controls.role.setValue('Manager');
    component.form.controls.isLoginEnabled.setValue(false);
    component.onToggleShop('s2', true);

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.editShopUserRequested({
        userId: 'u-staff',
        payload: {
          email: 'sales.updated@test.com',
          firstName: 'Sales',
          lastName: 'Updated',
          phoneNumber: '+15551234569',
          role: 'Manager',
          isLoginEnabled: false,
          shopIds: ['s1', 's2'],
        },
      })
    );
  });
});
