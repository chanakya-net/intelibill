import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { ExternalAuthProvider } from '../../../core/auth/auth.models';
import { AccountSecurityPageComponent } from './account-security-page.component';

describe('AccountSecurityPageComponent', () => {
  const auth = {
    session: () => ({ user: { id: 'actor', email: 'old@example.com' } }),
    isAuthenticated: () => true,
    getExternalLinks: vi.fn(),
    reauthenticate: vi.fn(),
  };
  const flow = { perform: vi.fn(), startProviderReauth: vi.fn() };
  function setup() {
    TestBed.configureTestingModule({
      imports: [AccountSecurityPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([]),
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
    auth.getExternalLinks.mockReset().mockReturnValue(of({ providers: [1], hasPassword: true }));
    auth.reauthenticate
      .mockReset()
      .mockReturnValue(
        of({ proof: 'recent', expiresAt: new Date(Date.now() + 300000).toISOString() }),
      );
    flow.perform.mockReset().mockReturnValue(of('email-pending'));
    flow.startProviderReauth.mockReset().mockReturnValue(of('redirecting'));
  });
  afterEach(() => TestBed.resetTestingModule());
  it('shows authoritative linked methods and current email', () => {
    const { component, fixture } = setup();
    expect(component.methods()?.providers).toEqual([1]);
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
  it('provider-only accounts reauthenticate with an already linked provider', () => {
    auth.getExternalLinks.mockReturnValue(of({ providers: [1], hasPassword: false }));
    const { component } = setup();
    component.chooseUnlink(ExternalAuthProvider.Google);
    component.confirmProvider(ExternalAuthProvider.Google);
    expect(flow.startProviderReauth).toHaveBeenCalledWith(1, { kind: 'unlink', provider: 1 });
  });
  it('unlink success redirects for new login', () => {
    flow.perform.mockReturnValue(of('security-updated'));
    const { component, navigate } = setup();
    component.chooseUnlink(1);
    component.passwordForm.controls.password.setValue('current');
    component.confirmPassword();
    expect(navigate).toHaveBeenCalledWith('/login?identity=updated');
  });
  it('last sign-in method failure leaves account intact and explains recovery', () => {
    flow.perform.mockReturnValue(throwError(() => ({ error: { title: 'Auth.LastSignInMethod' } })));
    const { component } = setup();
    component.chooseUnlink(1);
    component.passwordForm.controls.password.setValue('current');
    component.confirmPassword();
    expect(component.error()).toBe('identity.lastMethod');
  });
  it('cannot confirm reauth before choosing an explicit security operation', () => {
    const { component } = setup();
    component.passwordForm.controls.password.setValue('current');
    component.confirmPassword();
    expect(auth.reauthenticate).not.toHaveBeenCalled();
  });
});
