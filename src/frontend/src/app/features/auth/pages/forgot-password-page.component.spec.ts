import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ForgotPasswordPageComponent } from './forgot-password-page.component';

describe('ForgotPasswordPageComponent', () => {
  const authService = {
    requestPasswordReset: vi.fn(),
  };

  const store = {
    selectSignal: vi.fn(() => signal(false)),
  };

  function setup() {
    TestBed.configureTestingModule({
      imports: [ForgotPasswordPageComponent, RouterTestingModule.withRoutes([]), TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Store, useValue: store },
      ],
    });

    const fixture = TestBed.createComponent(ForgotPasswordPageComponent);
    fixture.detectChanges();

    return {
      component: fixture.componentInstance,
      fixture,
    };
  }

  beforeEach(() => {
    authService.requestPasswordReset.mockReset();
  });

  it('marks form as touched on submit with invalid form', () => {
    const { component } = setup();

    component.form.controls.email.setValue('');
    component.onSubmit();

    expect(component.form.controls.email.touched).toBe(true);
    expect(authService.requestPasswordReset).not.toHaveBeenCalled();
  });

  it('submits a trimmed email on valid form', () => {
    const { component } = setup();

    authService.requestPasswordReset.mockReturnValue(of(void 0));
    component.form.controls.email.setValue('  user@example.com  ');
    component.onSubmit();

    expect(authService.requestPasswordReset).toHaveBeenCalledWith('user@example.com');
  });

  it('shows a neutral success message after submit succeeds', () => {
    const { component } = setup();

    authService.requestPasswordReset.mockReturnValue(of(void 0));
    component.form.controls.email.setValue('user@example.com');
    component.onSubmit();

    expect(component.successMessage()).toBe('auth.forgotPassword.successMessage');
    expect(component.errorMessage()).toBeNull();
    expect(component.form.controls.email.value).toBe('');
  });

  it('shows a generic error message for non-rate-limited failures', () => {
    const { component } = setup();

    authService.requestPasswordReset.mockReturnValue(throwError(() => ({ status: 500 })));
    component.form.controls.email.setValue('user@example.com');
    component.onSubmit();

    expect(component.successMessage()).toBeNull();
    expect(component.errorMessage()).toBe('auth.forgotPassword.errorMessage');
  });

  it('maps 429 failures to the rate-limit message', () => {
    const { component } = setup();

    authService.requestPasswordReset.mockReturnValue(throwError(() => ({ status: 429 })));
    component.form.controls.email.setValue('user@example.com');
    component.onSubmit();

    expect(component.successMessage()).toBeNull();
    expect(component.errorMessage()).toBe('auth.forgotPassword.rateLimitMessage');
  });
});
