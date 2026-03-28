import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload, AuthUser } from '../../../core/auth/auth.models';
import { UserAccountService } from '../services/user-account.service';

@Component({
  selector: 'app-update-profile-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './update-profile-overlay.component.html',
  styleUrl: './update-profile-overlay.component.scss',
})
export class UpdateProfileOverlayComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly userAccountService = inject(UserAccountService);

  readonly isSubmitting = signal(false);
  readonly serverError = signal('');

  @Input({ required: true }) user!: AuthUser;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    phoneNumber: ['', [Validators.maxLength(32), Validators.pattern(/^\+?[0-9]{7,15}$/)]],
  });

  ngOnInit(): void {
    this.form.patchValue({
      firstName: this.user.firstName,
      lastName: this.user.lastName,
      email: this.user.email ?? '',
      phoneNumber: this.user.phoneNumber ?? '',
    });
  }

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
      firstName: this.form.controls.firstName.value.trim(),
      lastName: this.form.controls.lastName.value.trim(),
      email: this.form.controls.email.value.trim(),
      phoneNumber: this.toNullable(this.form.controls.phoneNumber.value),
    };

    this.userAccountService.updateMyProfile(payload).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.closeRequested.emit();
      },
      error: (error: { error?: ApiErrorPayload }) => {
        this.serverError.set(getProfileUpdateErrorMessage(error.error));
        this.isSubmitting.set(false);
      },
    });
  }

  private toNullable(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}

function getProfileUpdateErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'This email is already used by another account.';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'This mobile number is already used by another account.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to update profile right now. Please try again.';
}
