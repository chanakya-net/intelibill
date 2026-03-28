import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { UserShop } from '../../../core/auth/auth.models';
import { RootState } from '../../../core/state/app.state';
import { CreateShopRequest } from '../services/shop.service';
import { ShopsActions } from '../state/shops.actions';
import {
  selectSelectedShopDetails,
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsLoadingDetails,
  selectShopsSubmitting,
} from '../state/shops.selectors';

@Component({
  selector: 'app-manage-shop-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './manage-shop-overlay.component.html',
  styleUrl: './manage-shop-overlay.component.scss',
})
export class ManageShopOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly isLoadingDetails = this.store.selectSignal(selectShopsLoadingDetails);
  readonly serverError = this.store.selectSignal(selectShopsErrorMessage);
  readonly selectedShopDetails = this.store.selectSignal(selectSelectedShopDetails);
  readonly lastMutationType = this.store.selectSignal(selectShopsLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectShopsLastMutationSucceeded);

  readonly selectedShopRole = signal<string>('');
  readonly isUpdatePending = signal(false);

  @Input({ required: true }) shops: readonly UserShop[] = [];
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    shopId: ['', [Validators.required]],
    name: ['', [Validators.required, Validators.maxLength(120)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pincode: ['', [Validators.required, Validators.maxLength(16)]],
    contactPerson: ['', [Validators.maxLength(120)]],
    mobileNumber: ['', [Validators.maxLength(32)]],
  });

  constructor() {
    effect(() => {
      const details = this.selectedShopDetails();
      const selectedShopId = this.form.controls.shopId.value;
      if (!details || details.shopId !== selectedShopId) {
        return;
      }

      this.form.patchValue({
        name: details.name,
        address: details.address,
        city: details.city,
        state: details.state,
        pincode: details.pincode,
        contactPerson: details.contactPerson ?? '',
        mobileNumber: details.mobileNumber ?? '',
      });
    });

    effect(() => {
      const isUpdateSuccess = this.lastMutationType() === 'update' && this.lastMutationSucceeded();
      if (!this.isUpdatePending() || !isUpdateSuccess || this.isSubmitting()) {
        return;
      }

      this.isUpdatePending.set(false);
      this.store.dispatch(ShopsActions.clearMutationStatus());
      this.closeRequested.emit();
    });
  }

  ngOnInit(): void {
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());

    if (this.shops.length === 0) {
      return;
    }

    const initialShopId = this.shops[0].shopId;
    this.form.controls.shopId.setValue(initialShopId);
    this.updateSelectedRole(initialShopId);
    this.store.dispatch(ShopsActions.selectShop({ shopId: initialShopId }));
    this.store.dispatch(ShopsActions.loadShopDetailsRequested({ shopId: initialShopId }));
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.closeRequested.emit();
  }

  onShopSelectionChange(): void {
    const shopId = this.form.controls.shopId.value;
    if (!shopId) {
      return;
    }

    this.updateSelectedRole(shopId);
    this.store.dispatch(ShopsActions.selectShop({ shopId }));
    this.store.dispatch(ShopsActions.loadShopDetailsRequested({ shopId }));
  }

  onSubmit(): void {
    if (this.isSubmitting() || this.isLoadingDetails()) {
      return;
    }

    if (!this.isSelectedShopOwner()) {
        this.store.dispatch(
          ShopsActions.updateShopFailed({
            errorMessage: 'Only shop owners can update shop details.',
          })
        );
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const shopId = this.form.controls.shopId.value;
    if (!shopId) {
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isUpdatePending.set(true);

    const payload: CreateShopRequest = {
      name: this.form.controls.name.value.trim(),
      address: this.form.controls.address.value.trim(),
      city: this.form.controls.city.value.trim(),
      state: this.form.controls.state.value.trim(),
      pincode: this.form.controls.pincode.value.trim(),
      contactPerson: this.toOptionalValue(this.form.controls.contactPerson.value),
      mobileNumber: this.toOptionalValue(this.form.controls.mobileNumber.value),
    };

    this.store.dispatch(ShopsActions.updateShopRequested({ shopId, payload }));
  }

  isSelectedShopOwner(): boolean {
    return this.selectedShopRole().toLowerCase() === 'owner';
  }

  private updateSelectedRole(shopId: string): void {
    const selected = this.shops.find((shop) => shop.shopId === shopId);
    this.selectedShopRole.set(selected?.role ?? '');
  }

  private toOptionalValue(value: string): string | undefined {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : undefined;
  }
}
