import { signal, Signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { EditShopUserOverlayComponent } from './edit-shop-user-overlay.component';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersMembershipRefusal,
  selectUsersSubmitting,
} from '../state/users.selectors';
import { MembershipRefusal } from '../state/users.reducer';

describe('EditShopUserOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const refusalSignal = signal<MembershipRefusal | null>(null);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectUsersSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectUsersErrorMessage) {
        return errorSignal;
      }

      if (selector === selectUsersMembershipRefusal) {
        return refusalSignal;
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

  function render(
    role = 'Staff',
    status = 'Active',
    membershipVersion = 4,
    en: Record<string, string> = {},
  ): ComponentFixture<EditShopUserOverlayComponent> {
    TestBed.configureTestingModule({
      imports: [
        EditShopUserOverlayComponent,
        TranslocoTestingModule.forRoot({
          langs: { en },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
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
      role,
      status,
      membershipVersion,
      shopIds: ['s1'],
    };
    fixture.detectChanges();
    return fixture;
  }

  function setup(role = 'Staff', status = 'Active', membershipVersion = 4): EditShopUserOverlayComponent {
    return render(role, status, membershipVersion).componentInstance;
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    errorSignal.set('');
    refusalSignal.set(null);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('editMemberNeverSendsGlobalIdentity', () => {
    const component = setup();

    component.form.controls.role.setValue('Manager');
    component.form.controls.status.setValue('Suspended');
    component.onSubmit();

    // The form holds the two shop-scoped values and the version, and no account fields at all.
    expect(Object.keys(component.form.controls).sort()).toEqual(['role', 'status']);
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.updateMembershipRequested({
        shopId: 's1',
        userId: 'u-staff',
        payload: { role: 'Manager', status: 'Suspended', expectedVersion: 4 },
      }),
    );

    const submitted = dispatch.mock.calls
      .map(([action]) => action as { type: string; payload?: Record<string, unknown> })
      .find((action) => action.type === UsersActions.updateMembershipRequested.type);
    expect(Object.keys(submitted?.payload ?? {}).sort()).toEqual([
      'expectedVersion',
      'role',
      'status',
    ]);
  });

  it('sends the version the row was read at, so a stale screen is refused', () => {
    const component = setup('Staff', 'Active', 9);

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.updateMembershipRequested({
        shopId: 's1',
        userId: 'u-staff',
        payload: { role: 'Staff', status: 'Active', expectedVersion: 9 },
      }),
    );
  });

  it('shows the concurrency conflict the server reports instead of retrying', () => {
    const component = setup();
    errorSignal.set('errors.users.membershipVersionConflict');

    expect(component.serverError()).toBe('errors.users.membershipVersionConflict');
  });

  const seatText = {
    'errors.users.memberCapacityReached': '{{activeMembers}} of {{memberAllowance}} seats are taken.',
    'errors.users.memberCapacityReachedNoDetail': 'No free seat.',
  };
  const errorText = (fixture: ComponentFixture<EditShopUserOverlayComponent>) =>
    (fixture.nativeElement as HTMLElement).querySelector('.ib-overlay__error')?.textContent?.trim() ?? null;

  it('shows the seat usage beside its own refused reinstatement', () => {
    refusalSignal.set({ shopId: 's1', userId: 'u-staff', memberCapacity: { memberAllowance: 3, activeMembers: 3 } });
    const fixture = render('Staff', 'Suspended', 1, seatText);

    expect(errorText(fixture)).toBe('3 of 3 seats are taken.');
  });

  it('shows the plain seat message when the refusal carries no usage', () => {
    refusalSignal.set({ shopId: 's1', userId: 'u-staff', memberCapacity: null });
    const fixture = render('Staff', 'Suspended', 1, seatText);

    expect(errorText(fixture)).toBe('No free seat.');
  });

  it('ignores a refusal that belongs to another member or another shop', () => {
    refusalSignal.set({ shopId: 's1', userId: 'u-other', memberCapacity: { memberAllowance: 3, activeMembers: 3 } });
    const fixture = render('Staff', 'Suspended', 1, seatText);
    expect(errorText(fixture)).toBeNull();

    refusalSignal.set({ shopId: 's2', userId: 'u-staff', memberCapacity: null });
    fixture.detectChanges();
    expect(errorText(fixture)).toBeNull();
  });

  it('drops the refusal when the editor moves to another member', () => {
    const fixture = render('Staff', 'Suspended', 1, seatText);
    dispatch.mockClear();

    fixture.componentRef.setInput('user', { ...fixture.componentInstance.user, userId: 'u-next' });
    fixture.detectChanges();

    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearMembershipRefusal());
  });

  it('clears the previous error and mutation status before submitting', () => {
    const component = setup();

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearError());
    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
  });

  it('does not submit while a change is already in flight', () => {
    const component = setup();
    isSubmittingSignal.set(true);
    dispatch.mockReset();

    component.onSubmit();

    expect(dispatch).not.toHaveBeenCalled();
  });

  it('normalizes a case-insensitive manager role when patching the form', () => {
    const component = setup('manager');

    expect(component.form.controls.role.value).toBe('Manager');
  });

  it('shows the membership status the row arrived with', () => {
    const component = setup('Staff', 'suspended');

    expect(component.form.controls.status.value).toBe('Suspended');
  });
});
