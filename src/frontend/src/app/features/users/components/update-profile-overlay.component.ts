import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { AuthUser } from '../../../core/auth/auth.models';
import { RootState } from '../../../core/state/app.state';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';

@Component({
  selector: 'app-update-profile-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './update-profile-overlay.component.html',
  styleUrl: './update-profile-overlay.component.scss',
})
export class UpdateProfileOverlayComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectUsersLastMutationSucceeded);
  readonly isUpdateProfilePending = signal(false);

  @Input({ required: true }) user!: AuthUser;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    phoneNumber: ['', [Validators.maxLength(32), Validators.pattern(/^\+?[0-9]{7,15}$/)]],
  });

  constructor() {
    effect(() => {
      const isUpdateProfileSuccess = this.lastMutationType() === 'update-profile' && this.lastMutationSucceeded();
      if (!this.isUpdateProfilePending() || !isUpdateProfileSuccess || this.isSubmitting()) {
        return;
      }

      this.isUpdateProfilePending.set(false);
      this.store.dispatch(UsersActions.clearMutationStatus());
      this.closeRequested.emit();
    });
  }

  ngOnInit(): void {
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
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

    const payload = {
      firstName: this.form.controls.firstName.value.trim(),
      lastName: this.form.controls.lastName.value.trim(),
      email: this.form.controls.email.value.trim(),
      phoneNumber: this.toNullable(this.form.controls.phoneNumber.value),
    };

    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.isUpdateProfilePending.set(true);
    this.store.dispatch(UsersActions.updateProfileRequested({ payload }));
  }

  private toNullable(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
