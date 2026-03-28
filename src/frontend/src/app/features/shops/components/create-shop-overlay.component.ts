import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { ShopService } from '../services/shop.service';

@Component({
  selector: 'app-create-shop-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './create-shop-overlay.component.html',
  styleUrl: './create-shop-overlay.component.scss',
})
export class CreateShopOverlayComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly shopService = inject(ShopService);

  readonly isSubmitting = signal(false);
  readonly serverError = signal('');

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pincode: ['', [Validators.required, Validators.maxLength(16)]],
    contactPerson: ['', [Validators.maxLength(120)]],
    mobileNumber: ['', [Validators.maxLength(32)]],
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
      name: this.form.controls.name.value.trim(),
      address: this.form.controls.address.value.trim(),
      city: this.form.controls.city.value.trim(),
      state: this.form.controls.state.value.trim(),
      pincode: this.form.controls.pincode.value.trim(),
      contactPerson: this.toOptionalValue(this.form.controls.contactPerson.value),
      mobileNumber: this.toOptionalValue(this.form.controls.mobileNumber.value),
    };

    this.shopService.createShop(payload).subscribe({
      next: () => {
        this.isSubmitting.set(false);
      },
      error: (error: { error?: ApiErrorPayload }) => {
        this.serverError.set(getShopCreateErrorMessage(error.error));
        this.isSubmitting.set(false);
      },
    });
  }

  private toOptionalValue(value: string): string | undefined {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : undefined;
  }
}

function getShopCreateErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Unauthorized' || title === 'Auth.Unauthorized') {
    return 'Your session could not be verified for shop creation. Please sign in again.';
  }

  if (title === 'Shop.NameRequired') {
    return 'Shop name is required.';
  }

  if (title === 'Shop.AddressRequired') {
    return 'Shop address is required.';
  }

  if (title === 'Shop.CityRequired') {
    return 'Shop city is required.';
  }

  if (title === 'Shop.StateRequired') {
    return 'Shop state is required.';
  }

  if (title === 'Shop.PincodeRequired') {
    return 'Shop pincode is required.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to create your shop right now. Please try again.';
}
