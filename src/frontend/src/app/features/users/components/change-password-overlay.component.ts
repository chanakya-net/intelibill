import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { UserAccountService } from '../services/user-account.service';

@Component({
  selector: 'app-change-password-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, PasswordModule, ProgressSpinnerModule],
  templateUrl: './change-password-overlay.component.html',
  styleUrl: './change-password-overlay.component.scss',
})
export class ChangePasswordOverlayComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly userAccountService = inject(UserAccountService);

  readonly isSubmitting = signal(false);
  readonly serverError = signal('');

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(100)]],
  });

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.closeRequested.emit();
  }

  onSubmit(): void {
    if (this.isSubmitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.serverError.set('');
    this.isSubmitting.set(true);

    const payload = {
      currentPassword: this.form.controls.currentPassword.value,
      newPassword: this.form.controls.newPassword.value,
    };

    this.userAccountService.changeMyPassword(payload).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.closeRequested.emit();
      },
      error: (error: { error?: ApiErrorPayload }) => {
        this.serverError.set(getChangePasswordErrorMessage(error.error));
        this.isSubmitting.set(false);
      },
    });
  }
}

function getChangePasswordErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.InvalidCurrentPassword') {
    return 'Current password is incorrect.';
  }

  if (title === 'Auth.PasswordNotSet') {
    return 'Password is not set for this account.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to change password right now. Please try again.';
}
