import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnInit, Output, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { ShopsActions } from '../state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsSubmitting,
} from '../state/shops.selectors';

const INDIA_GST_REGEX = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/i;

@Component({
  selector: 'app-create-shop-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './create-shop-overlay.component.html',
  styleUrl: './create-shop-overlay.component.scss',
})
export class CreateShopOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly serverError = this.store.selectSignal(selectShopsErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectShopsLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectShopsLastMutationSucceeded);
  readonly isCreatePending = signal(false);

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pincode: ['', [Validators.required, Validators.maxLength(16)]],
    contactPerson: ['', [Validators.maxLength(120)]],
    mobileNumber: ['', [Validators.maxLength(32)]],
    gstNumber: ['', [Validators.maxLength(20), Validators.pattern(INDIA_GST_REGEX)]],
  });

  constructor() {
    effect(() => {
      const isCreateSuccess = this.lastMutationType() === 'create' && this.lastMutationSucceeded();
      if (!this.isCreatePending() || !isCreateSuccess || this.isSubmitting()) {
        return;
      }

      this.isCreatePending.set(false);
      this.store.dispatch(ShopsActions.clearMutationStatus());
      this.closeRequested.emit();
    });
  }

  ngOnInit(): void {
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
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

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isCreatePending.set(true);

    const payload = {
      name: this.form.controls.name.value.trim(),
      address: this.form.controls.address.value.trim(),
      city: this.form.controls.city.value.trim(),
      state: this.form.controls.state.value.trim(),
      pincode: this.form.controls.pincode.value.trim(),
      contactPerson: this.toOptionalValue(this.form.controls.contactPerson.value),
      mobileNumber: this.toOptionalValue(this.form.controls.mobileNumber.value),
      gstNumber: this.toOptionalValue(this.form.controls.gstNumber.value),
    };

    this.store.dispatch(ShopsActions.createShopRequested({ payload }));
  }

  private toOptionalValue(value: string): string | undefined {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : undefined;
  }
}
