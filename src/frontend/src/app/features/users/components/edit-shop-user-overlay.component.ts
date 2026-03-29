import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { ShopUser } from '../services/user-account.service';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';

@Component({
  selector: 'app-edit-shop-user-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, CheckboxModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './edit-shop-user-overlay.component.html',
  styleUrl: './edit-shop-user-overlay.component.scss',
})
export class EditShopUserOverlayComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectUsersLastMutationSucceeded);
  readonly isEditUserPending = signal(false);

  @Input({ required: true }) user!: ShopUser;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    phoneNumber: ['', [Validators.required, Validators.maxLength(32), Validators.pattern(/^\+?[0-9]{7,15}$/)]],
    role: ['Manager' as 'Manager' | 'SalesPerson', [Validators.required]],
    isLoginEnabled: [true],
  });

  constructor() {
    effect(() => {
      const isSuccess = this.lastMutationType() === 'edit-shop-user' && this.lastMutationSucceeded();
      if (!this.isEditUserPending() || !isSuccess || this.isSubmitting()) {
        return;
      }

      this.isEditUserPending.set(false);
      this.store.dispatch(UsersActions.clearMutationStatus());
      this.closeRequested.emit();
    });
  }

  ngOnInit(): void {
    this.patchFormFromUser();
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['user']) {
      return;
    }

    this.patchFormFromUser();
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
    this.isEditUserPending.set(true);

    this.store.dispatch(
      UsersActions.editShopUserRequested({
        userId: this.user.userId,
        payload: {
          firstName: this.form.controls.firstName.value.trim(),
          lastName: this.form.controls.lastName.value.trim(),
          phoneNumber: this.form.controls.phoneNumber.value.trim(),
          role: this.form.controls.role.value,
          isLoginEnabled: this.form.controls.isLoginEnabled.value,
        },
      })
    );
  }

  private patchFormFromUser(): void {
    if (!this.user) {
      return;
    }

    this.form.patchValue({
      firstName: this.user.firstName,
      lastName: this.user.lastName,
      phoneNumber: this.user.phoneNumber ?? '',
      role: this.user.role === 'Manager' ? 'Manager' : 'SalesPerson',
      isLoginEnabled: this.user.isLoginEnabled,
    });
  }
}
