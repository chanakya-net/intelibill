import { TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { VerifyEmailPageComponent } from './verify-email-page.component';

describe('VerifyEmailPageComponent', () => {
  const auth = {
    isAuthenticated: vi.fn(),
    confirmEmailEnrollment: vi.fn(),
    confirmEmailChange: vi.fn(),
    clearSession: vi.fn(),
  };
  function setup(fragment = 'challengeId=id&token=bearer&purpose=enrollment') {
    TestBed.configureTestingModule({
      imports: [VerifyEmailPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: ActivatedRoute, useValue: { snapshot: { fragment } } },
      ],
    });
    const location = TestBed.inject(Location);
    const scrub = vi.spyOn(location, 'replaceState');
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(VerifyEmailPageComponent);
    fixture.detectChanges();
    return {
      component: fixture.componentInstance,
      fixture,
      scrub,
      navigate,
      flow: TestBed.inject(IdentityFlowService),
    };
  }
  beforeEach(() => {
    sessionStorage.clear();
    auth.isAuthenticated.mockReset().mockReturnValue(false);
    auth.confirmEmailEnrollment.mockReset().mockReturnValue(of({}));
    auth.confirmEmailChange.mockReset().mockReturnValue(of({}));
  });
  afterEach(() => TestBed.resetTestingModule());
  it('scrubs bearer fragment immediately and waits for owner-chosen credentials', () => {
    const { component, scrub } = setup();
    expect(scrub).toHaveBeenCalledWith('/auth/verify-email');
    expect(auth.confirmEmailEnrollment).not.toHaveBeenCalled();
    component.form.patchValue({
      firstName: 'Ada',
      lastName: 'L',
      password: 'Chosen123!',
      confirmPassword: 'Chosen123!',
    });
    component.onSubmit();
    expect(auth.confirmEmailEnrollment).toHaveBeenCalledWith(
      {
        challengeId: 'id',
        token: 'bearer',
        password: 'Chosen123!',
        firstName: 'Ada',
        lastName: 'L',
      },
      false,
    );
  });
  it('claim completion is explicit and uses newly chosen credentials', () => {
    const { component } = setup('challengeId=id&token=bearer&purpose=claim');
    component.form.patchValue({
      firstName: 'Ada',
      lastName: 'L',
      password: 'Chosen123!',
      confirmPassword: 'Chosen123!',
    });
    component.onSubmit();
    expect(auth.confirmEmailEnrollment).toHaveBeenLastCalledWith(
      expect.objectContaining({ password: 'Chosen123!' }),
      true,
    );
  });
  it('rejects whitespace-only names and mismatched passwords', () => {
    const { component } = setup();
    component.form.patchValue({
      firstName: ' ',
      lastName: 'L',
      password: 'Chosen123!',
      confirmPassword: 'Different123!',
    });
    component.onSubmit();
    expect(component.form.invalid).toBe(true);
    expect(auth.confirmEmailEnrollment).not.toHaveBeenCalled();
  });
  it('anonymous email change requires original account login with safe resume', () => {
    const { component, flow, navigate } = setup('challengeId=id&token=bearer&purpose=email-change');
    component.onSubmit();
    expect(auth.confirmEmailChange).not.toHaveBeenCalled();
    expect(flow.pendingVerification()?.token).toBe('bearer');
    component.signIn();
    expect(navigate).toHaveBeenCalledWith('/login');
  });
  it('resumes email change after login and logs out after explicit successful confirmation', () => {
    const { component, flow, navigate } = setup('challengeId=id&token=bearer&purpose=email-change');
    auth.isAuthenticated.mockReturnValue(true);
    component.onSubmit();
    expect(auth.confirmEmailChange).toHaveBeenCalledWith('id', 'bearer');
    expect(flow.pendingVerification()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/login?identity=updated');
  });
  it('wrong actor cannot consume pending proof and sees original-account guidance', () => {
    auth.isAuthenticated.mockReturnValue(true);
    auth.confirmEmailChange.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.WrongChallengeActor' } })),
    );
    const { component, flow } = setup('challengeId=id&token=bearer&purpose=email-change');
    component.onSubmit();
    expect(component.error()).toBe('identity.wrongAccount');
    expect(flow.pendingVerification()?.token).toBe('bearer');
  });
  it('expired or replayed confirmation explains a fresh request without retrying credentials', () => {
    auth.confirmEmailEnrollment.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.InvalidChallenge' } })),
    );
    const { component } = setup();
    component.form.patchValue({
      firstName: 'Ada',
      lastName: 'L',
      password: 'Chosen123!',
      confirmPassword: 'Chosen123!',
    });
    component.onSubmit();
    expect(component.error()).toBe('identity.invalidLink');
    expect(component.form.controls.password.value).toBe('');
  });
  it('malformed link never calls confirmation endpoints', () => {
    const { component } = setup('token=oops&purpose=unknown');
    component.onSubmit();
    expect(component.error()).toBe('identity.invalidLink');
    expect(auth.confirmEmailEnrollment).not.toHaveBeenCalled();
  });
  it('operator recovery requirement clears unusable pending change and preserves support guidance', () => {
    auth.isAuthenticated.mockReturnValue(true);
    auth.confirmEmailChange.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.RecoverySupportRequired' } })),
    );
    const { component, flow } = setup('challengeId=id&token=bearer&purpose=email-change');
    component.onSubmit();
    expect(component.error()).toBe('identity.supportRequired');
    expect(flow.pendingVerification()).toBeNull();
    expect(component.valid()).toBe(false);
  });
});
