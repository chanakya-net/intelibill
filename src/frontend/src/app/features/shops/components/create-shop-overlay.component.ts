import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
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

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
  });

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

    const name = this.form.controls.name.value.trim();
    this.shopService.createShop(name).subscribe({
      next: () => {
        this.isSubmitting.set(false);
      },
      error: (error: { error?: ApiErrorPayload }) => {
        this.serverError.set(getShopCreateErrorMessage(error.error));
        this.isSubmitting.set(false);
      },
    });
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

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to create your shop right now. Please try again.';
}
