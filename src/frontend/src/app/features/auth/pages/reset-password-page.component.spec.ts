import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ResetPasswordPageComponent } from './reset-password-page.component';

describe('ResetPasswordPageComponent', () => {
  const authService = {
    resetPassword: vi.fn(),
  };

  const store = {
    selectSignal: vi.fn(() => signal(false)),
  };

  function setup(queryParams: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [
        ResetPasswordPageComponent,
        RouterTestingModule.withRoutes([]),
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Store, useValue: store },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap(queryParams),
            },
          },
        },
      ],
    });

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ResetPasswordPageComponent);
    fixture.detectChanges();

    return {
      component: fixture.componentInstance,
      fixture,
      navigate,
    };
  }

  beforeEach(() => {
    authService.resetPassword.mockReset();
  });

  it('shows the generic invalid-or-expired message when email or token is missing', () => {
    const { component } = setup({ email: 'user@example.com' });

    expect(component.errorMessage()).toBe('auth.resetPassword.invalidLinkMessage');
    expect(component.canSubmit()).toBe(false);
  });

  it('marks the form invalid when the passwords do not match', () => {
    const { component } = setup({ email: 'user@example.com', token: 'token-123' });

    component.form.controls.password.setValue('Password123!');
    component.form.controls.confirmPassword.setValue('Mismatch123!');

    expect(component.form.valid).toBe(false);
    expect(component.form.errors).toEqual({ passwordMismatch: true });
  });

  it('submits the reset request and navigates back to login on success', () => {
    const { component, navigate } = setup({ email: 'user@example.com', token: 'token-123' });

    authService.resetPassword.mockReturnValue(of(void 0));
    component.form.controls.password.setValue('Password123!');
    component.form.controls.confirmPassword.setValue('Password123!');
    component.onSubmit();

    expect(authService.resetPassword).toHaveBeenCalledWith(
      'user@example.com',
      'token-123',
      'Password123!',
    );
    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { passwordReset: 'success' },
    });
  });

  it('maps 401 failures to the generic invalid-or-expired message', () => {
    const { component } = setup({ email: 'user@example.com', token: 'token-123' });

    authService.resetPassword.mockReturnValue(throwError(() => ({ status: 401 })));
    component.form.controls.password.setValue('Password123!');
    component.form.controls.confirmPassword.setValue('Password123!');
    component.onSubmit();

    expect(component.errorMessage()).toBe('auth.resetPassword.invalidLinkMessage');
  });

  it('maps invalid token errors to the generic invalid-or-expired message', () => {
    const { component } = setup({ email: 'user@example.com', token: 'token-123' });

    authService.resetPassword.mockReturnValue(
      throwError(() => ({ status: 400, error: { title: 'Auth.InvalidPasswordResetToken' } })),
    );
    component.form.controls.password.setValue('Password123!');
    component.form.controls.confirmPassword.setValue('Password123!');
    component.onSubmit();

    expect(component.errorMessage()).toBe('auth.resetPassword.invalidLinkMessage');
  });

  it('maps 429 failures to the rate-limit message', () => {
    const { component } = setup({ email: 'user@example.com', token: 'token-123' });

    authService.resetPassword.mockReturnValue(throwError(() => ({ status: 429 })));
    component.form.controls.password.setValue('Password123!');
    component.form.controls.confirmPassword.setValue('Password123!');
    component.onSubmit();

    expect(component.errorMessage()).toBe('auth.resetPassword.rateLimitMessage');
  });
});
