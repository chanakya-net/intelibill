import { Component, EventEmitter, OnInit, Output, inject, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { Customer } from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';

@Component({
  selector: 'app-edit-customer-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    CheckboxModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './edit-customer-overlay.component.html',
  styleUrl: './edit-customer-overlay.component.scss',
})
export class EditCustomerOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly customersFacade = inject(CustomersFacade);

  readonly customer = input.required<Customer>();

  readonly isSubmitting = this.customersFacade.submitting;
  readonly serverError = this.customersFacade.errorMessage;

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(180)]],
    phoneNumber: ['', [Validators.required, Validators.maxLength(32), Validators.pattern(/^[+]?\d{7,15}$/)]],
    address: ['', [Validators.maxLength(320)]],
    isActive: [true],
  });

  ngOnInit(): void {
    this.customersFacade.clearError();
    this.customersFacade.clearMutationStatus();

    const c = this.customer();
    this.form.patchValue({
      name: c.name,
      phoneNumber: c.phoneNumber,
      address: c.address ?? '',
      isActive: c.isActive,
    });
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
    this.customersFacade.clearError();
    this.customersFacade.clearMutationStatus();
    this.customersFacade.editCustomer(this.customer().customerId, {
      name: this.form.controls.name.value.trim(),
      phoneNumber: this.form.controls.phoneNumber.value.trim(),
      address: this.nullableTrimmed(this.form.controls.address.value),
      isActive: this.form.controls.isActive.value,
    });
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
