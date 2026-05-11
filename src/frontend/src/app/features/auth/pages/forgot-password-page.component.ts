import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { RootState } from '../../../core/state/app.state';

@Component({
  selector: 'app-forgot-password-page',
  standalone: true,
  imports: [ReactiveFormsModule, InputTextModule, ButtonModule, RouterLink, ProgressSpinnerModule, TranslocoPipe],
  templateUrl: './forgot-password-page.component.html',
  styleUrl: './forgot-password-page.component.scss',
})
export class ForgotPasswordPageComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);

  readonly isLoading = this.store.selectSignal((state) => state.httpUi.pendingRequests > 0);
  readonly supportedLanguages = this.localizationService.supportedLanguages;
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly nativeLanguageNames = NATIVE_LANGUAGE_NAMES;

  readonly successMessage = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  readonly emailInputPt = {
    root: { class: 'forgot-password-email-input' },
  };

  readonly progressSpinnerPt = {
    root: { class: 'auth-spinner-root' },
  };

  ngOnInit(): void {
    this.form.valueChanges.subscribe(() => {
      this.successMessage.set(null);
      this.errorMessage.set(null);
    });
  }

  onSubmit(): void {
    const email = this.form.controls.email.value.trim();
    this.form.controls.email.setValue(email, { emitEvent: false });

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.successMessage.set(null);
    this.errorMessage.set(null);

    this.authService.requestPasswordReset(email).subscribe({
      next: () => {
        this.form.controls.email.setValue('', { emitEvent: false });
        this.form.markAsPristine();
        this.form.markAsUntouched();
        this.successMessage.set('auth.forgotPassword.successMessage');
      },
      error: (error: { status?: number }) => {
        this.successMessage.set(null);
        if (error.status === 429) {
          this.errorMessage.set('auth.forgotPassword.rateLimitMessage');
        } else {
          this.errorMessage.set('auth.forgotPassword.errorMessage');
        }
      },
    });
  }

  async onLanguageChanged(language: string): Promise<void> {
    await this.localizationService.setLanguage(language as SupportedLanguage);
  }
}
