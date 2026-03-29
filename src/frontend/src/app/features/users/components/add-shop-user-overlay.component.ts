import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnInit, Output, effect, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
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

@Component({
  selector: 'app-add-shop-user-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, PasswordModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './add-shop-user-overlay.component.html',
  styleUrl: './add-shop-user-overlay.component.scss',
})
export class AddShopUserOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectUsersLastMutationSucceeded);
  readonly isAddUserPending = signal(false);

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    phoneNumber: ['', [Validators.required, Validators.maxLength(32), Validators.pattern(/^\+?[0-9]{7,15}$/)]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(100)]],
    confirmPassword: ['', [Validators.required]],
    role: ['Manager' as 'Manager' | 'SalesPerson', [Validators.required]],
  }, { validators: passwordsMatchValidator });

  constructor() {
    effect(() => {
      const isSuccess = this.lastMutationType() === 'add-shop-user' && this.lastMutationSucceeded();
      if (!this.isAddUserPending() || !isSuccess || this.isSubmitting()) {
        return;
      }

      this.isAddUserPending.set(false);
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

    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.isAddUserPending.set(true);
    this.store.dispatch(
      UsersActions.addShopUserRequested({
        payload: {
          firstName: this.form.controls.firstName.value.trim(),
          lastName: this.form.controls.lastName.value.trim(),
          phoneNumber: this.form.controls.phoneNumber.value.trim(),
          password: this.form.controls.password.value,
          confirmPassword: this.form.controls.confirmPassword.value,
          role: this.form.controls.role.value,
        },
      })
    );
  }
}

function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;

  if (!password || !confirmPassword) {
    return null;
  }

  return password === confirmPassword ? null : { passwordMismatch: true };
}
