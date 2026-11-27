import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { PhoneChallengeReceipt } from '../../../core/auth/auth.models';
import { PhoneVerificationPageComponent } from './phone-verification-page.component';

describe('PhoneVerificationPageComponent', () => {
  const auth = {
    getPhoneAuthAvailability: vi.fn(),
    requestPhoneChallenge: vi.fn(),
    confirmPhoneLogin: vi.fn(),
    confirmPhoneEnrollment: vi.fn(),
  };

  function receipt(overrides: Partial<PhoneChallengeReceipt> = {}): PhoneChallengeReceipt {
    return {
      challengeId: 'challenge-1',
      maskedDestination: '+91•••••••210',
      resendAllowedAt: new Date(Date.now() + 45_000).toISOString(),
      expiresAt: new Date(Date.now() + 300_000).toISOString(),
      ...overrides,
    };
  }

  function setup() {
    TestBed.configureTestingModule({
      imports: [PhoneVerificationPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([]),
        { provide: Store, useValue: { dispatch: vi.fn(), select: vi.fn(), selectSignal: vi.fn() } },
        { provide: AuthService, useValue: auth },
      ],
    });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(PhoneVerificationPageComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, navigate };
  }

  beforeEach(() => {
    auth.getPhoneAuthAvailability.mockReset().mockReturnValue(of({ enabled: true }));
    auth.requestPhoneChallenge.mockReset().mockReturnValue(of(receipt()));
    auth.confirmPhoneLogin.mockReset().mockReturnValue(of({}));
    auth.confirmPhoneEnrollment.mockReset().mockReturnValue(of({}));
  });

  afterEach(() => TestBed.resetTestingModule());

  it('showsUnavailableStateWhenPhoneAuthDisabled', () => {
    auth.getPhoneAuthAvailability.mockReturnValue(of({ enabled: false }));
    const { component, fixture } = setup();

    expect(component.available()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('identity.phoneUnavailableHere');
    expect(auth.requestPhoneChallenge).not.toHaveBeenCalled();
    fixture.destroy();
  });

  it('masksTheDestinationItWasGiven', () => {
    const { component, fixture } = setup();
    component.phoneForm.setValue({ phoneNumber: '+919876543210' });
    component.requestCode();
    fixture.detectChanges();

    // Whatever is shown comes from the server's masked value; the page never renders the number
    // the holder typed.
    expect(component.receipt()?.maskedDestination).toBe('+91•••••••210');
    expect(fixture.nativeElement.textContent).toContain('+91•••••••210');
    expect(fixture.nativeElement.textContent).not.toContain('+919876543210');
    fixture.destroy();
  });

  it('resendWaitUsesServerDeadline', () => {
    const { component, fixture } = setup();
    component.phoneForm.setValue({ phoneNumber: '+919876543210' });
    component.requestCode();

    // Driven by resendAllowedAt, not by when this page rendered.
    expect(component.resendSeconds()).toBeGreaterThan(40);
    expect(component.resendSeconds()).toBeLessThanOrEqual(45);
    expect(component.canResend()).toBe(false);

    auth.requestPhoneChallenge.mockReturnValue(
      of(receipt({ resendAllowedAt: new Date(Date.now() - 1_000).toISOString() })),
    );
    component.requestCode();

    expect(component.resendSeconds()).toBe(0);
    expect(component.canResend()).toBe(true);
    fixture.destroy();
  });

  it('otpFailureDoesNotErasePendingWork', () => {
    const { component, fixture } = setup();
    component.setMode('enrollment');
    component.phoneForm.setValue({ phoneNumber: '+919876543210' });
    component.requestCode();

    component.codeForm.setValue({ code: '000000', firstName: 'Ada', lastName: 'Lovelace' });
    auth.confirmPhoneEnrollment.mockReturnValue(
      throwError(() => ({ error: { code: 'Auth.InvalidChallenge' } })),
    );
    component.confirmCode();

    expect(component.error()).toBe('identity.invalidLink');
    // The challenge, the number and the names survive a mistyped digit; only the code is cleared.
    expect(component.receipt()?.challengeId).toBe('challenge-1');
    expect(component.phoneForm.getRawValue().phoneNumber).toBe('+919876543210');
    expect(component.codeForm.getRawValue().firstName).toBe('Ada');
    expect(component.codeForm.getRawValue().lastName).toBe('Lovelace');
    expect(component.codeForm.getRawValue().code).toBe('');
    fixture.destroy();
  });

  it('signs in on a correct code and leaves the page', () => {
    const { component, fixture, navigate } = setup();
    component.phoneForm.setValue({ phoneNumber: '+919876543210' });
    component.requestCode();
    component.codeForm.patchValue({ code: '123456' });
    component.confirmCode();

    expect(auth.confirmPhoneLogin).toHaveBeenCalledWith('challenge-1', '123456');
    expect(component.receipt()).toBeNull();
    expect(navigate).toHaveBeenCalled();
    fixture.destroy();
  });

  it('asks for a name before enrolling', () => {
    const { component, fixture } = setup();
    component.setMode('enrollment');
    component.phoneForm.setValue({ phoneNumber: '+919876543210' });
    component.requestCode();
    component.codeForm.setValue({ code: '123456', firstName: '  ', lastName: '' });
    component.confirmCode();

    expect(auth.confirmPhoneEnrollment).not.toHaveBeenCalled();
    fixture.destroy();
  });
});
