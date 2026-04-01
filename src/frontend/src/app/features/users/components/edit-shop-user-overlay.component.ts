import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, computed, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { ShopUser } from '../services/user-account.service';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersSubmitting,
} from '../state/users.selectors';

@Component({
  selector: 'app-edit-shop-user-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, CheckboxModule, ButtonModule, ProgressSpinnerModule, TranslocoPipe],
  templateUrl: './edit-shop-user-overlay.component.html',
  styleUrl: './edit-shop-user-overlay.component.scss',
})
export class EditShopUserOverlayComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly availableShops = computed(() => this.authService.session()?.shops ?? []);

  @Input({ required: true }) user!: ShopUser;
  @Output() readonly closeRequested = new EventEmitter<void>();

  private selectedShopIds: string[] = [];

  readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    phoneNumber: ['', [Validators.required, Validators.maxLength(32), Validators.pattern(/^\+?[0-9]{7,15}$/)]],
    role: ['Manager' as 'Manager' | 'Staff', [Validators.required]],
    isLoginEnabled: [true],
  });

  constructor() {}

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

    this.store.dispatch(
      UsersActions.editShopUserRequested({
        userId: this.user.userId,
        payload: {
          email: this.form.controls.email.value.trim(),
          firstName: this.form.controls.firstName.value.trim(),
          lastName: this.form.controls.lastName.value.trim(),
          phoneNumber: this.form.controls.phoneNumber.value.trim(),
          role: this.form.controls.role.value,
          isLoginEnabled: this.form.controls.isLoginEnabled.value,
          shopIds: [...this.selectedShopIds],
        },
      })
    );
  }

  isShopSelected(shopId: string): boolean {
    return this.selectedShopIds.includes(shopId);
  }

  onToggleShop(shopId: string, isChecked: boolean): void {
    if (isChecked && !this.selectedShopIds.includes(shopId)) {
      this.selectedShopIds = [...this.selectedShopIds, shopId];
      return;
    }

    if (!isChecked && this.selectedShopIds.includes(shopId)) {
      this.selectedShopIds = this.selectedShopIds.filter((id) => id !== shopId);
    }
  }

  private patchFormFromUser(): void {
    if (!this.user) {
      return;
    }

    this.selectedShopIds = [...(this.user.shopIds ?? [])];

    this.form.patchValue({
      email: this.user.email ?? '',
      firstName: this.user.firstName,
      lastName: this.user.lastName,
      phoneNumber: this.user.phoneNumber ?? '',
      role: this.user.role === 'Manager' ? 'Manager' : 'Staff',
      isLoginEnabled: this.user.isLoginEnabled,
    });
  }
}
