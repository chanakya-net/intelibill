import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { RootState } from '../../../core/state/app.state';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { InputValidators } from '../../../shared/forms/input-validation';
import { RegisterActions } from '../state/register.actions';
import {
  selectRegisterErrorMessage,
  selectRegisterSubmitting,
  selectRegisterAccepted,
  selectRegisterEmail,
  selectRegisterClaim,
} from '../state/register.selectors';
@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [ReactiveFormsModule, InputTextModule, ButtonModule, RouterLink, TranslocoPipe],
  templateUrl: './register-page.component.html',
  styleUrl: './register-page.component.scss',
})
export class RegisterPageComponent implements OnInit {
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);
  readonly serverError = this.store.selectSignal(selectRegisterErrorMessage);
  readonly isSubmitting = this.store.selectSignal(selectRegisterSubmitting);
  readonly accepted = this.store.selectSignal(selectRegisterAccepted);
  readonly email = this.store.selectSignal(selectRegisterEmail);
  readonly claim = this.store.selectSignal(selectRegisterClaim);
  readonly supportedLanguages = this.localizationService.supportedLanguages;
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly nativeLanguageNames = NATIVE_LANGUAGE_NAMES;
  readonly form = inject(FormBuilder).nonNullable.group({ email: ['', InputValidators.email()] });
  ngOnInit(): void {
    this.store.dispatch(RegisterActions.clearError());
  }
  onSubmit(): void {
    this.request(false);
  }
  onClaim(): void {
    this.request(true);
  }
  onResend(): void {
    if (!this.isSubmitting())
      this.store.dispatch(RegisterActions.requested({ email: this.email(), claim: this.claim() }));
  }
  onStartOver(): void {
    this.store.dispatch(RegisterActions.reset());
  }
  async onLanguageChanged(language: string): Promise<void> {
    await this.localizationService.setLanguage(language as SupportedLanguage);
  }
  private request(claim: boolean): void {
    if (this.isSubmitting()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.store.dispatch(
      RegisterActions.requested({ email: this.form.controls.email.value.trim(), claim }),
    );
  }
}
