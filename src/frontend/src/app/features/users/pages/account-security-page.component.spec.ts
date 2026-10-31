import { signal, Signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { SessionService } from '../services/session.service';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';
import { AccountSecurityPageComponent } from './account-security-page.component';

describe('AccountSecurityPageComponent', () => {
  const auth = {
    session: () => ({
      user: {
        id: 'actor',
        email: 'old@example.com',
        firstName: 'Test',
        lastName: 'User',
        phoneNumber: '+15551234567',
      },
    }),
    isAuthenticated: () => true,
    getSignInMethods: vi.fn(),
    reauthenticate: vi.fn(),
  };
  const flow = { perform: vi.fn() };
  const sessions = {
    getMySessions: vi.fn(),
    revokeSession: vi.fn(),
    revokeAllSessions: vi.fn(),
  };
  const dispatch = vi.fn();
  const submittingSignal = signal(false);
  const profileErrorSignal = signal('');
  const lastMutationTypeSignal = signal<'update-profile' | 'change-password' | null>(null);
  const lastMutationSucceededSignal = signal(false);
  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectUsersSubmitting) return submittingSignal;
      if (selector === selectUsersErrorMessage) return profileErrorSignal;
      if (selector === selectUsersLastMutationType) return lastMutationTypeSignal;
      if (selector === selectUsersLastMutationSucceeded) return lastMutationSucceededSignal;
      return signal(undefined);
    }),
  };
  function setup(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [AccountSecurityPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(query) } },
        },
        { provide: AuthService, useValue: auth },
        { provide: IdentityFlowService, useValue: flow },
        { provide: SessionService, useValue: sessions },
        { provide: Store, useValue: store },
      ],
    });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(AccountSecurityPageComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, fixture, navigate };
  }
  beforeEach(() => {
    auth.getSignInMethods.mockReset().mockReturnValue(of({ hasPassword: true }));
    auth.reauthenticate
      .mockReset()
      .mockReturnValue(
        of({ proof: 'recent', expiresAt: new Date(Date.now() + 300000).toISOString() }),
      );
    flow.perform.mockReset().mockReturnValue(of('email-pending'));
    sessions.getMySessions.mockReset().mockReturnValue(of([]));
    sessions.revokeSession.mockReset().mockReturnValue(of(void 0));
    sessions.revokeAllSessions.mockReset().mockReturnValue(of(void 0));
    dispatch.mockReset();
    store.selectSignal.mockClear();
    submittingSignal.set(false);
    profileErrorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
  });
  afterEach(() => TestBed.resetTestingModule());
  it('shows the authoritative sign-in method and current email', () => {
    const { component, fixture } = setup();
    expect(component.methods()?.hasPassword).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('old@example.com');
  });
  it('starts the profile form from the signed-in account', () => {
    const { component } = setup();
    expect(component.profileForm.getRawValue()).toEqual({
      firstName: 'Test',
      lastName: 'User',
      email: 'old@example.com',
      phoneNumber: '+15551234567',
    });
  });
  it('saves trimmed names and phone on their own', () => {
    const { component, fixture } = setup();
    component.profileForm.controls.firstName.setValue('  Jane  ');
    component.profileForm.controls.lastName.setValue('  Doe  ');
    component.profileForm.controls.phoneNumber.setValue('  +15557654321  ');
    expect(component.emailChangeRequested()).toBe(false);
    component.saveProfile();
    expect(component.operation()).toBeNull();
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.updateProfileRequested({
        payload: {
          firstName: 'Jane',
          lastName: 'Doe',
          email: 'old@example.com',
          phoneNumber: '+15557654321',
          language: 'en-IN',
        },
      }),
    );
    lastMutationTypeSignal.set('update-profile');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();
    expect(component.profileNotice()).toBe('profile.profileSaved');
    expect(dispatch).toHaveBeenCalledWith(UsersActions.clearMutationStatus());
  });
  it('does not save an incomplete profile', () => {
    const { component } = setup();
    component.profileForm.controls.firstName.setValue('   ');
    component.saveProfile();
    expect(component.profileForm.touched).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: UsersActions.updateProfileRequested.type }),
    );
  });
  it('shows the profile save failure the store reports', () => {
    const { component, fixture } = setup();
    profileErrorSignal.set('users.errors.emailTaken');
    fixture.detectChanges();
    expect(component.profileError()).toBe('users.errors.emailTaken');
    expect(fixture.nativeElement.textContent).toContain('users.errors.emailTaken');
  });
  it('a new address turns the save into a verified email change', () => {
    const { component } = setup();
    component.profileForm.controls.email.setValue('new@example.com');
    expect(component.emailChangeRequested()).toBe(true);
    component.saveProfile();
    expect(dispatch).toHaveBeenCalledWith(
      UsersActions.updateProfileRequested({
        payload: expect.objectContaining({ email: 'old@example.com' }),
      }),
    );
    expect(component.operation()).toEqual({ kind: 'email', email: 'new@example.com' });
  });
  it('the same address in a different case is not an email change', () => {
    const { component } = setup();
    component.profileForm.controls.email.setValue('  OLD@Example.com  ');
    expect(component.emailChangeRequested()).toBe(false);
  });
  it('pending email uses recent proof and never updates current email', () => {
    const { component } = setup();
    component.profileForm.controls.email.setValue('new@example.com');
    component.saveProfile();
    component.passwordForm.controls.password.setValue('current');
    component.confirmPassword();
    expect(auth.reauthenticate).toHaveBeenCalledWith('current');
    expect(flow.perform).toHaveBeenCalledWith(
      { kind: 'email', email: 'new@example.com' },
      expect.objectContaining({ proof: 'recent' }),
    );
    expect(component.notice()).toBe('identity.emailPending');
    expect(auth.session().user.email).toBe('old@example.com');
    expect(component.profileForm.controls.email.value).toBe('old@example.com');
    expect(component.passwordForm.controls.password.value).toBe('');
  });
  it('an account without a password cannot start a reauthenticated change', () => {
    auth.getSignInMethods.mockReturnValue(of({ hasPassword: false }));
    const { component, fixture } = setup();
    expect(component.methods()?.hasPassword).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('identity.noPassword');
  });
  it('a rejected proof leaves the account intact and explains recovery', () => {
    flow.perform.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.RecoverySupportRequired' } })),
    );
    const { component } = setup();
    component.profileForm.controls.email.setValue('new@example.com');
    component.saveProfile();
    component.passwordForm.controls.password.setValue('current');
    component.confirmPassword();
    expect(component.error()).toBe('identity.supportRequired');
  });
  it('cannot confirm reauth before choosing an explicit security operation', () => {
    const { component } = setup();
    component.passwordForm.controls.password.setValue('current');
    component.confirmPassword();
    expect(auth.reauthenticate).not.toHaveBeenCalled();
  });
  it('lists the signed-in devices and ends one of them', () => {
    sessions.getMySessions.mockReturnValue(
      of([
        {
          sessionId: 'other',
          deviceLabel: 'Shop counter',
          userAgent: 'Chrome',
          isCurrent: false,
          lastSeenAt: new Date().toISOString(),
        },
      ]),
    );
    const { component, fixture } = setup();
    expect(fixture.nativeElement.textContent).toContain('Shop counter');
    component.endDevice(component.devices()[0]);
    expect(sessions.revokeSession).toHaveBeenCalledWith('other');
  });
  it.each([
    ['support-required', 'identity.supportRequired'],
    ['delivery-unavailable', 'identity.deliveryUnavailable'],
    ['failed', 'identity.reauthFailed'],
  ])('renders safe operation outcome %s', (reason, message) => {
    const { component, fixture } = setup({ identity: reason });
    expect(component.error()).toBe(message);
    expect(fixture.nativeElement.textContent).toContain(message);
  });
  it('ignores non-allowlisted outcome text', () => {
    expect(setup({ identity: 'private-token-do-not-display' }).component.error()).toBe('');
  });
});
