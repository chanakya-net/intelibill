import { computed, Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { RootState } from '../../../core/state/app.state';

@Component({
  selector: 'app-reset-password-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    PasswordModule,
    ButtonModule,
    RouterLink,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './reset-password-page.component.html',
  styleUrl: './reset-password-page.component.scss',
})
export class ResetPasswordPageComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);

  readonly isLoading = this.store.selectSignal((state) => state.httpUi.pendingRequests > 0);
  readonly supportedLanguages = this.localizationService.supportedLanguages;
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly nativeLanguageNames = NATIVE_LANGUAGE_NAMES;

  readonly errorMessage = signal<string | null>(null);
  private readonly resetEmail = signal<string | null>(null);
  private readonly resetToken = signal<string | null>(null);
  private readonly linkValid = signal(true);
  readonly canSubmit = computed(
    () => !!this.resetEmail() && !!this.resetToken() && this.linkValid(),
  );

  readonly form = this.formBuilder.nonNullable.group(
    {
      password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(100)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: passwordsMatchValidator },
  );

  readonly passwordInputPt = {
    root: { class: 'register-password-root' },
  };

  readonly confirmPasswordInputPt = {
    root: { class: 'register-confirm-password-root' },
  };

  readonly progressSpinnerPt = {
    root: { class: 'auth-spinner-root' },
  };

  ngOnInit(): void {
    const queryParamMap = this.route.snapshot.queryParamMap;
    const email = queryParamMap.get('email')?.trim() ?? '';
    const token = queryParamMap.get('token')?.trim() ?? '';

    this.resetEmail.set(email || null);
    this.resetToken.set(token || null);

    if (!email || !token) {
      this.invalidateLink();
    }
  }

  onSubmit(): void {
    if (!this.canSubmit()) {
      this.form.markAllAsTouched();
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.errorMessage.set(null);

    this.authService
      .resetPassword(this.resetEmail()!, this.resetToken()!, this.form.controls.password.value)
      .subscribe({
        next: () => {
          void this.router.navigate(['/login'], {
            queryParams: { passwordReset: 'success' },
          });
        },
        error: (error: { status?: number; error?: ApiErrorPayload }) => {
          if (error.status === 429) {
            this.errorMessage.set('auth.resetPassword.rateLimitMessage');
            return;
          }

          if (error.status === 401 || error.error?.title === 'Auth.InvalidPasswordResetToken') {
            this.invalidateLink();
            return;
          }

          this.errorMessage.set('auth.resetPassword.invalidLinkMessage');
        },
      });
  }

  async onLanguageChanged(language: string): Promise<void> {
    await this.localizationService.setLanguage(language as SupportedLanguage);
  }

  private invalidateLink(): void {
    this.linkValid.set(false);
    this.errorMessage.set('auth.resetPassword.invalidLinkMessage');
  }
}

function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;

  if (!password || !confirmPassword) {
    return null;
  }

  return password === confirmPassword ? null : { passwordMismatch: true };
}
