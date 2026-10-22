import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { RegisterActions } from '../state/register.actions';
import { selectRegisterErrorMessage, selectRegisterSubmitting } from '../state/register.selectors';
import { InputValidators } from '../../../shared/forms/input-validation';

@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    PasswordModule,
    CheckboxModule,
    ButtonModule,
    RouterLink,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './register-page.component.html',
  styleUrl: './register-page.component.scss',
})
export class RegisterPageComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);

  readonly serverError = this.store.selectSignal(selectRegisterErrorMessage);
  readonly isSubmitting = this.store.selectSignal(selectRegisterSubmitting);
  readonly supportedLanguages = this.localizationService.supportedLanguages;
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly nativeLanguageNames = NATIVE_LANGUAGE_NAMES;

  readonly form = this.formBuilder.nonNullable.group(
    {
      firstName: ['', InputValidators.requiredText(100)],
      lastName: ['', InputValidators.requiredText(100)],
      email: ['', InputValidators.email()],
      phoneNumber: ['', InputValidators.requiredText(20)],
      password: ['', InputValidators.password()],
      confirmPassword: ['', InputValidators.password()],
      rememberMe: [true],
    },
    { validators: InputValidators.fieldsMatch('password', 'confirmPassword', 'passwordMismatch') },
  );

  readonly firstNameInputPt = {
    root: { class: 'register-first-name-input' },
  };

  readonly lastNameInputPt = {
    root: { class: 'register-last-name-input' },
  };

  readonly emailInputPt = {
    root: { class: 'register-email-input' },
  };

  readonly phoneNumberInputPt = {
    root: { class: 'register-phone-number-input' },
  };

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
    this.store.dispatch(RegisterActions.clearError());
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { firstName, lastName, email, phoneNumber, password, rememberMe } =
      this.form.getRawValue();

    this.store.dispatch(RegisterActions.clearError());
    this.store.dispatch(
      RegisterActions.requested({
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        phoneNumber: phoneNumber.trim(),
        password,
        rememberMe,
      }),
    );
  }

  async onLanguageChanged(language: string): Promise<void> {
    await this.localizationService.setLanguage(language as SupportedLanguage);
  }
}
