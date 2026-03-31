import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { RootState } from '../../../core/state/app.state';
import { Supplier } from '../services/supplier.service';
import { SuppliersActions } from '../state/suppliers.actions';
import {
  selectSuppliersErrorMessage,
  selectSuppliersSubmitting,
} from '../state/suppliers.selectors';

@Component({
  selector: 'app-edit-supplier-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, CheckboxModule, ButtonModule, ProgressSpinnerModule, TranslocoPipe],
  templateUrl: './edit-supplier-overlay.component.html',
  styleUrl: './edit-supplier-overlay.component.scss',
})
export class EditSupplierOverlayComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectSuppliersSubmitting);
  readonly serverError = this.store.selectSignal(selectSuppliersErrorMessage);

  @Input({ required: true }) supplier!: Supplier;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    contactPersonName: ['', [Validators.maxLength(120)]],
    contactPersonPhone: ['', [Validators.maxLength(32), Validators.pattern(/^\+?[0-9]{7,15}$/)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pin: ['', [Validators.required, Validators.maxLength(16)]],
    isActive: [true],
    isPreferred: [false],
  });

  constructor() {}

  ngOnInit(): void {
    this.patchForm();
    this.store.dispatch(SuppliersActions.clearError());
    this.store.dispatch(SuppliersActions.clearMutationStatus());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['supplier']) {
      return;
    }

    this.patchForm();
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

    this.store.dispatch(SuppliersActions.clearError());
    this.store.dispatch(SuppliersActions.clearMutationStatus());
    this.store.dispatch(
      SuppliersActions.editSupplierRequested({
        supplierId: this.supplier.supplierId,
        payload: {
          name: this.form.controls.name.value.trim(),
          contactPersonName: this.nullableTrimmed(this.form.controls.contactPersonName.value),
          contactPersonPhone: this.nullableTrimmed(this.form.controls.contactPersonPhone.value),
          address: this.form.controls.address.value.trim(),
          city: this.form.controls.city.value.trim(),
          state: this.form.controls.state.value.trim(),
          pin: this.form.controls.pin.value.trim(),
          isActive: this.form.controls.isActive.value,
          isPreferred: this.form.controls.isPreferred.value,
        },
      })
    );
  }

  private patchForm(): void {
    if (!this.supplier) {
      return;
    }

    this.form.patchValue({
      name: this.supplier.name,
      contactPersonName: this.supplier.contactPersonName ?? '',
      contactPersonPhone: this.supplier.contactPersonPhone ?? '',
      address: this.supplier.address,
      city: this.supplier.city,
      state: this.supplier.state,
      pin: this.supplier.pin,
      isActive: this.supplier.isActive,
      isPreferred: this.supplier.isPreferred,
    });
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}