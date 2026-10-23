import {
  identityError,
  identityFailureReason,
  identityFailureMessage,
} from '../../../core/auth/identity-errors';
import { Location } from '@angular/common';
import { timeout } from 'rxjs';
import { Component, InjectionToken, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload, ExternalAuthProvider } from '../../../core/auth/auth.models';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { InputValidators } from '../../../shared/forms/input-validation';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { RootState } from '../../../core/state/app.state';

export type ExternalLoginRedirect = (authorizationUrl: string) => void;

export const EXTERNAL_LOGIN_REDIRECT = new InjectionToken<ExternalLoginRedirect>(
  'EXTERNAL_LOGIN_REDIRECT',
  {
    factory:
      () =>
      (authorizationUrl: string): void => {
        window.location.assign(authorizationUrl);
      },
  },
);

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [
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
  private readonly externalLoginRedirect = inject(EXTERNAL_LOGIN_REDIRECT);

  private readonly location = inject(Location);
  private readonly identityFlow = inject(IdentityFlowService);

  readonly serverError = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);
  readonly isHttpLoading = this.store.selectSignal((state) => state.httpUi.pendingRequests > 0);
  readonly supportedLanguages = this.localizationService.supportedLanguages;
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly nativeLanguageNames = NATIVE_LANGUAGE_NAMES;

  readonly form = this.formBuilder.nonNullable.group({
    identifier: ['', InputValidators.requiredText()],
    password: ['', InputValidators.requiredText()],
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
    const callbackCode = this.route.snapshot.queryParamMap.get('code');
    const callbackState = this.route.snapshot.queryParamMap.get('state');
    if (
      this.identityFlow.hasOAuthIntent() &&
      (callbackCode || callbackState || this.route.snapshot.queryParamMap.get('error'))
    ) {
      this.location.replaceState('/login');
      if (!callbackCode || !callbackState) {
        this.failIdentityCallback();
        return;
      }
      this.identityFlow
        .completeOAuth(callbackCode, callbackState)
        .pipe(timeout(15000))
        .subscribe({
          next: (result) => {
            if (result === 'redirecting') return;
            if (result === 'security-updated') {
              this.serverError.set(null);
              this.successMessage.set('identity.signInAgain');
            }
            void this.router.navigateByUrl(
              result === 'security-updated'
                ? '/login?identity=updated'
                : '/account/security?identity=pending',
            );
          },
          error: (error) => this.failIdentityCallback(error),
        });
      return;
    }
    if (this.authService.isAuthenticated()) {
      void this.router.navigateByUrl(this.identityFlow.afterSignIn());
      return;
    }

    const rememberedIdentifier = this.authService.getLastRememberedIdentifier();
    if (rememberedIdentifier) {
      this.form.controls.identifier.setValue(rememberedIdentifier);
      this.form.controls.rememberMe.setValue(true);
    }

    const code = this.route.snapshot.queryParamMap.get('code');
    const state = this.route.snapshot.queryParamMap.get('state');
    if (code && state) {
      this.location.replaceState('/login');
      this.authService.completeExternalLogin(code, state).subscribe({
        next: () => {
          this.clearExternalPendingMarker();
          void this.router.navigateByUrl(this.identityFlow.afterSignIn());
        },
        error: (error: { error?: ApiErrorPayload }) => {
          this.clearExternalPendingMarker();
          this.serverError.set(getExternalCallbackErrorMessage(error.error));
        },
      });
      return;
    }

    const identityStatus = this.route.snapshot.queryParamMap.get('identity');
    if (identityStatus === 'updated') this.successMessage.set('identity.signInAgain');
    const failureMessage = identityFailureMessage(identityStatus);
    if (failureMessage) this.serverError.set(failureMessage);
    if (this.identityFlow.pendingVerification())
      this.successMessage.set('identity.originalAccountRequired');
    const passwordResetSuccess =
      this.route.snapshot.queryParamMap.get('passwordReset') === 'success' ||
      !!this.router.getCurrentNavigation()?.extras.state?.['passwordResetSuccess'];
    if (passwordResetSuccess) {
      this.successMessage.set('auth.resetPassword.successMessage');
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
      const pendingExternalAuth = sessionStorage.getItem(
        LoginPageComponent.ExternalPendingStorageKey,
      );
      if (pendingExternalAuth) {
        this.serverError.set('errors.auth.externalSignInIncomplete');
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
    this.successMessage.set(null);

    const { identifier, password, rememberMe } = this.form.getRawValue();

    this.authService.login(identifier.trim(), password, rememberMe).subscribe({
      next: () => {
        void this.router.navigateByUrl(this.identityFlow.afterSignIn());
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

    if (error.startsWith('errors.') || error.startsWith('identity.')) {
      return this.localizationService.translate(error);
    }

    return error;
  }

  private failIdentityCallback(error?: Parameters<typeof identityError>[0]): void {
    this.identityFlow.clearOAuth();
    const reason = identityFailureReason(error);
    this.successMessage.set(null);
    this.serverError.set(identityFailureMessage(reason));
    void this.router.navigateByUrl(
      `${this.authService.isAuthenticated() ? '/account/security' : '/login'}?identity=${reason}`,
    );
  }

  private startExternalLogin(provider: ExternalAuthProvider): void {
    this.serverError.set(null);
    this.successMessage.set(null);

    try {
      sessionStorage.setItem(LoginPageComponent.ExternalPendingStorageKey, provider.toString());
    } catch {
      // Ignore storage failures.
    }

    this.authService.initializeExternalLogin(provider).subscribe({
      next: (authorizationUrl) => {
        this.externalLoginRedirect(authorizationUrl);
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
  const title = error?.code ?? error?.title ?? '';
  if (title === 'Auth.AccountLinkRequired') return 'identity.collision';
  if (title === 'Auth.EmailVerificationRequired') return 'identity.verificationRequired';
  if (title === 'Auth.RecoverySupportRequired') return 'identity.supportRequired';

  if (title === 'Auth.InvalidCredentials') {
    return 'errors.auth.invalidCredentials';
  }

  if (title === 'Auth.UserLoginDisabled') {
    return 'errors.auth.userLoginDisabled';
  }

  if (title === 'Auth.UnsupportedProvider') {
    return 'errors.auth.unsupportedProvider';
  }

  if (title === 'Auth.ExternalProviderError') {
    return 'errors.auth.externalProviderError';
  }

  return 'errors.auth.unableToSignIn';
}

function getExternalCallbackErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.code ?? error?.title ?? '';
  if (title === 'Auth.AccountLinkRequired') return 'identity.collision';
  if (title === 'Auth.EmailVerificationRequired') return 'identity.verificationRequired';
  if (title === 'Auth.RecoverySupportRequired') return 'identity.supportRequired';

  if (title === 'Auth.ExternalStateInvalid') {
    return 'errors.auth.externalStateInvalid';
  }

  if (title === 'Auth.ExternalProviderError') {
    return 'errors.auth.externalProviderError';
  }

  return 'errors.auth.unableToCompleteExternalSignIn';
}
