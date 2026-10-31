import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { AccountSecurityPageComponent } from './account-security-page.component';

describe('AccountSecurityPageComponent', () => {
  const auth = {
    session: () => ({ user: { id: 'actor', email: 'old@example.com' } }),
    isAuthenticated: () => true,
    getSignInMethods: vi.fn(),
    reauthenticate: vi.fn(),
  };
  const flow = { perform: vi.fn() };
  function setup(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [AccountSecurityPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(query) } },
        },
        { provide: AuthService, useValue: auth },
        { provide: IdentityFlowService, useValue: flow },
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
  });
  afterEach(() => TestBed.resetTestingModule());
  it('shows the authoritative sign-in method and current email', () => {
    const { component, fixture } = setup();
    expect(component.methods()?.hasPassword).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('old@example.com');
  });
  it('pending email uses recent proof and never updates current email', () => {
    const { component } = setup();
    component.emailForm.controls.email.setValue('new@example.com');
    component.chooseEmailChange();
    component.passwordForm.controls.password.setValue('current');
    component.confirmPassword();
    expect(auth.reauthenticate).toHaveBeenCalledWith('current');
    expect(flow.perform).toHaveBeenCalledWith(
      { kind: 'email', email: 'new@example.com' },
      expect.objectContaining({ proof: 'recent' }),
    );
    expect(component.notice()).toBe('identity.emailPending');
    expect(auth.session().user.email).toBe('old@example.com');
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
    component.emailForm.controls.email.setValue('new@example.com');
    component.chooseEmailChange();
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
