import { identityFailureMessage } from '../../../core/auth/identity-errors';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { AuthService } from '../../../core/auth/auth.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { InputValidators } from '../../../shared/forms/input-validation';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { RootState } from '../../../core/state/app.state';

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
  private readonly authService = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);

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
    if (this.authService.isAuthenticated()) {
      void this.router.navigateByUrl(this.identityFlow.afterSignIn());
      return;
    }

    const rememberedIdentifier = this.authService.getLastRememberedIdentifier();
    if (rememberedIdentifier) {
      this.form.controls.identifier.setValue(rememberedIdentifier);
      this.form.controls.rememberMe.setValue(true);
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
}

function getAuthErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.code ?? error?.title ?? '';
  if (title === 'Auth.EmailVerificationRequired') return 'identity.verificationRequired';
  if (title === 'Auth.RecoverySupportRequired') return 'identity.supportRequired';

  if (title === 'Auth.InvalidCredentials') {
    return 'errors.auth.invalidCredentials';
  }

  if (title === 'Auth.UserLoginDisabled') {
    return 'errors.auth.userLoginDisabled';
  }

  return 'errors.auth.unableToSignIn';
}
