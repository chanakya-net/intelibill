import { Component, OnInit, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { EmailVerification, IdentityFlowService } from '../../../core/auth/identity-flow.service';
import { identityError } from '../../../core/auth/identity-errors';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { NATIVE_LANGUAGE_NAMES, SupportedLanguage } from '../../../core/i18n/language.constants';
import { InputValidators } from '../../../shared/forms/input-validation';
@Component({
  selector: 'app-verify-email-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    ButtonModule,
    InputTextModule,
    PasswordModule,
  ],
  templateUrl: './verify-email-page.component.html',
  styleUrl: './verify-email-page.component.scss',
})
export class VerifyEmailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  private readonly flow = inject(IdentityFlowService);
  private readonly localizationService = inject(LocalizationService);
  readonly supportedLanguages = this.localizationService.supportedLanguages;
  readonly currentLanguage = this.localizationService.currentLanguage;
  readonly nativeLanguageNames = NATIVE_LANGUAGE_NAMES;
  private proof: EmailVerification | null = null;
  readonly error = signal('');
  readonly busy = signal(false);
  readonly emailChange = signal(false);
  readonly valid = signal(false);
  readonly form = inject(FormBuilder).nonNullable.group(
    {
      firstName: ['', InputValidators.requiredText(100)],
      lastName: ['', InputValidators.requiredText(100)],
      password: ['', InputValidators.password()],
      confirmPassword: ['', InputValidators.password()],
    },
    { validators: InputValidators.fieldsMatch('password', 'confirmPassword', 'passwordMismatch') },
  );
  ngOnInit(): void {
    const fragment = this.route.snapshot.fragment;
    this.location.replaceState('/auth/verify-email');
    if (fragment) {
      const params = new URLSearchParams(fragment);
      const purpose = params.get('purpose');
      if (
        ['enrollment', 'claim', 'email-change'].includes(purpose ?? '') &&
        params.getAll('token').length === 1 &&
        params.getAll('challengeId').length === 1 &&
        params.getAll('purpose').length === 1 &&
        params.get('challengeId') &&
        params.get('token')
      )
        this.proof = {
          challengeId: params.get('challengeId')!,
          token: params.get('token')!,
          purpose: purpose as EmailVerification['purpose'],
        };
    } else this.proof = this.flow.pendingVerification();
    if (!this.proof) {
      this.error.set('identity.invalidLink');
      return;
    }
    this.valid.set(true);
    this.emailChange.set(this.proof.purpose === 'email-change');
    if (this.emailChange()) this.flow.saveVerification(this.proof);
  }
  onSubmit(): void {
    if (!this.proof || !this.valid() || this.busy()) return;
    if (this.emailChange()) {
      if (!this.auth.isAuthenticated()) return;
      this.busy.set(true);
      this.error.set('');
      this.auth
        .confirmEmailChange(this.proof.challengeId, this.proof.token)
        .pipe(finalize(() => this.busy.set(false)))
        .subscribe({
          next: () => {
            this.flow.clearVerification();
            this.proof = null;
            void this.router.navigateByUrl('/login?identity=updated');
          },
          error: (error) => {
            this.error.set(identityError(error));
            if (
              this.error() === 'identity.invalidLink' ||
              this.error() === 'identity.supportRequired'
            ) {
              this.flow.clearVerification();
              this.valid.set(false);
            }
          },
        });
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { password, firstName, lastName } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set('');
    this.auth
      .confirmEmailEnrollment(
        {
          challengeId: this.proof.challengeId,
          token: this.proof.token,
          password,
          firstName: firstName.trim(),
          lastName: lastName.trim(),
        },
        this.proof.purpose === 'claim',
      )
      .pipe(
        finalize(() => {
          this.busy.set(false);
          this.form.controls.password.reset();
          this.form.controls.confirmPassword.reset();
        }),
      )
      .subscribe({
        next: () => {
          this.proof = null;
          void this.router.navigateByUrl('/');
        },
        error: (error) => {
          this.error.set(identityError(error));
          if (this.error() === 'identity.invalidLink') this.valid.set(false);
        },
      });
  }
  signIn(): void {
    if (this.proof) this.flow.saveVerification(this.proof);
    void this.router.navigateByUrl('/login');
  }
  switchAccount(): void {
    this.auth.clearSession();
    this.signIn();
  }
  async onLanguageChanged(language: string): Promise<void> {
    await this.localizationService.setLanguage(language as SupportedLanguage);
  }
}
