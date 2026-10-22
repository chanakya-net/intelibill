import { Component, EventEmitter, OnInit, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { SuppliersFacade } from '../state/suppliers.facade';
import { InputValidators } from '../../../shared/forms/input-validation';

@Component({
  selector: 'app-add-supplier-overlay',
  standalone: true,
  imports: [ReactiveFormsModule, InputTextModule, CheckboxModule, ButtonModule, TranslocoPipe],
  templateUrl: './add-supplier-overlay.component.html',
  styleUrl: './add-supplier-overlay.component.scss',
})
export class AddSupplierOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly suppliersFacade = inject(SuppliersFacade);

  readonly isSubmitting = this.suppliersFacade.isSubmitting;
  readonly serverError = this.suppliersFacade.errorMessage;

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', InputValidators.requiredText(180)],
    contactPersonName: ['', InputValidators.optionalText(120)],
    contactPersonPhone: ['', InputValidators.phoneNumber({ required: false, maxLength: 32 })],
    address: ['', InputValidators.requiredText(320)],
    city: ['', InputValidators.requiredText(120)],
    state: ['', InputValidators.requiredText(120)],
    pin: ['', InputValidators.requiredText(16)],
    isActive: [true],
    isPreferred: [false],
  });

  constructor() {}

  ngOnInit(): void {
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
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
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.suppliersFacade.addSupplier({
      name: this.form.controls.name.value.trim(),
      contactPersonName: this.nullableTrimmed(this.form.controls.contactPersonName.value),
      contactPersonPhone: this.nullableTrimmed(this.form.controls.contactPersonPhone.value),
      address: this.form.controls.address.value.trim(),
      city: this.form.controls.city.value.trim(),
      state: this.form.controls.state.value.trim(),
      pin: this.form.controls.pin.value.trim(),
      isActive: this.form.controls.isActive.value,
      isPreferred: this.form.controls.isPreferred.value,
    });
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
