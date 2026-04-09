import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload, ExternalAuthProvider } from '../../../core/auth/auth.models';
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
  private static readonly ExternalErrorStorageKey = 'inventory.auth.external.error';
  private static readonly ExternalPendingStorageKey = 'inventory.auth.external.pending';

  private readonly authService = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
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

  readonly emailInputPt = {
    root: { class: 'login-email-input' },
  };

  readonly passwordInputPt = {
    root: { class: 'login-password-root' },
  };

  readonly progressSpinnerPt = {
    root: { class: 'auth-spinner-root' },
  };

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

    const code = this.route.snapshot.queryParamMap.get('code');
    const state = this.route.snapshot.queryParamMap.get('state');
    if (code && state) {
      this.authService.completeExternalLogin(code, state).subscribe({
        next: () => {
          this.clearExternalPendingMarker();
          void this.router.navigateByUrl('/');
        },
        error: (error: { error?: ApiErrorPayload }) => {
          this.clearExternalPendingMarker();
          this.serverError.set(getExternalCallbackErrorMessage(error.error));
        },
      });
      return;
    }

    let externalAuthError = this.route.snapshot.queryParamMap.get('externalAuthError');

    if (!externalAuthError) {
      try {
        externalAuthError = sessionStorage.getItem(LoginPageComponent.ExternalErrorStorageKey);
      } catch {
        externalAuthError = null;
      }
    }

    if (externalAuthError) {
      this.serverError.set(externalAuthError);

      try {
        sessionStorage.removeItem(LoginPageComponent.ExternalErrorStorageKey);
      } catch {
        // Ignore storage failures.
      }
      return;
    }

    try {
      const pendingExternalAuth = sessionStorage.getItem(LoginPageComponent.ExternalPendingStorageKey);
      if (pendingExternalAuth) {
        this.serverError.set('External sign-in did not complete. Please try again.');
        sessionStorage.removeItem(LoginPageComponent.ExternalPendingStorageKey);
      }
    } catch {
      // Ignore storage failures.
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

  onGoogleLogin(): void {
    this.startExternalLogin(ExternalAuthProvider.Google);
  }

  onFacebookLogin(): void {
    this.startExternalLogin(ExternalAuthProvider.Facebook);
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

  private startExternalLogin(provider: ExternalAuthProvider): void {
    this.serverError.set(null);

    try {
      sessionStorage.setItem(LoginPageComponent.ExternalPendingStorageKey, provider.toString());
    } catch {
      // Ignore storage failures.
    }

    this.authService.initializeExternalLogin(provider).subscribe({
      next: (authorizationUrl) => {
        window.location.assign(authorizationUrl);
      },
      error: (error: { error?: ApiErrorPayload }) => {
        this.clearExternalPendingMarker();
        this.serverError.set(getAuthErrorMessage(error.error));
      },
    });
  }

  private clearExternalPendingMarker(): void {
    try {
      sessionStorage.removeItem(LoginPageComponent.ExternalPendingStorageKey);
    } catch {
      // Ignore storage failures.
    }
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

  if (title === 'Auth.UnsupportedProvider') {
    return 'Google login is not enabled on the server.';
  }

  if (title === 'Auth.ExternalProviderError') {
    return error?.detail ?? 'External login configuration is invalid on the server.';
  }

  return 'errors.auth.unableToSignIn';
}

function getExternalCallbackErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.ExternalStateInvalid') {
    return 'Your sign-in session expired. Please try again.';
  }

  if (title === 'Auth.ExternalProviderError') {
    return error?.detail ?? 'Unable to complete external sign-in.';
  }

  return 'Unable to complete external sign-in.';
}
