import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { RootState } from '../../../core/state/app.state';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    InputTextModule,
    PasswordModule,
    ButtonModule,
    RouterLink,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
})
export class LoginPageComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);

  readonly serverError = signal<string | null>(null);
  readonly isHttpLoading = this.store.selectSignal((state) => state.httpUi.pendingRequests > 0);
  readonly supportedLanguages = this.localizationService.supportedLanguages;
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly nativeLanguageNames = NATIVE_LANGUAGE_NAMES;

  readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    rememberMe: [true],
  });

  ngOnInit(): void {
    if (this.authService.isAuthenticated()) {
      void this.router.navigateByUrl('/');
      return;
    }

    const rememberedEmail = this.authService.getLastRememberedEmail();
    if (rememberedEmail) {
      this.form.controls.email.setValue(rememberedEmail);
      this.form.controls.rememberMe.setValue(true);
    }
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.serverError.set(null);

    const { email, password, rememberMe } = this.form.getRawValue();

    this.authService.loginWithEmail(email.trim(), password, rememberMe).subscribe({
      next: () => {
        void this.router.navigateByUrl('/');
      },
      error: (error: { error?: ApiErrorPayload }) => {
        this.serverError.set(getAuthErrorMessage(error.error));
      },
    });
  }

  async onLanguageChanged(language: string): Promise<void> {
    await this.localizationService.setLanguage(language as SupportedLanguage);
  }

  getServerErrorMessage(): string {
    const error = this.serverError();
    if (!error) {
      return '';
    }

    if (error.startsWith('errors.')) {
      return this.localizationService.translate(error);
    }

    return error;
  }
}

function getAuthErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.InvalidCredentials') {
    return 'errors.auth.invalidCredentials';
  }

  if (title === 'Auth.UserLoginDisabled') {
    return 'errors.auth.userLoginDisabled';
  }

  return 'errors.auth.unableToSignIn';
}
