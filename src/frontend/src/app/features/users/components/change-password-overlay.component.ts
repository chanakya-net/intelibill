import { Component, EventEmitter, OnInit, Output, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';
import { InputValidators } from '../../../shared/forms/input-validation';

@Component({
  selector: 'app-change-password-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ButtonModule,
    PasswordModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './change-password-overlay.component.html',
  styleUrl: './change-password-overlay.component.scss',
})
export class ChangePasswordOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectUsersLastMutationSucceeded);
  readonly isChangePasswordPending = signal(false);

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group(
    {
      currentPassword: ['', InputValidators.requiredText()],
      newPassword: ['', InputValidators.password()],
      confirmNewPassword: ['', InputValidators.password()],
    },
    {
      validators: [
        InputValidators.fieldsMatch('newPassword', 'confirmNewPassword', 'passwordMismatch'),
        InputValidators.fieldsDiffer('currentPassword', 'newPassword', 'sameAsCurrent'),
      ],
    },
  );

  readonly progressSpinnerPt = {
    root: { class: 'change-password-spinner-root' },
  };

  constructor() {
    effect(() => {
      const isChangePasswordSuccess =
        this.lastMutationType() === 'change-password' && this.lastMutationSucceeded();
      if (!this.isChangePasswordPending() || !isChangePasswordSuccess || this.isSubmitting()) {
        return;
      }

      this.isChangePasswordPending.set(false);
      this.store.dispatch(UsersActions.clearMutationStatus());
      this.closeRequested.emit();
    });
  }

  ngOnInit(): void {
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
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
      currentPassword: this.form.controls.currentPassword.value,
      newPassword: this.form.controls.newPassword.value,
    };

    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.isChangePasswordPending.set(true);
    this.store.dispatch(UsersActions.changePasswordRequested({ payload }));
  }
}
