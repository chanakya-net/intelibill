import { Component, EventEmitter, OnInit, Output, inject, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';

import { Customer } from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';
import { InputValidators } from '../../../shared/forms/input-validation';
import { GST_STATE_OPTIONS } from '../../../shared/gst/gst-state-codes';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
} from '../../../shared/primeng-pt.config';

@Component({
  selector: 'app-edit-customer-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    CheckboxModule,
    ButtonModule,
    ProgressSpinnerModule,
    InputNumberModule,
    InputGroupModule,
    InputGroupAddonModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './edit-customer-overlay.component.html',
  styleUrl: './edit-customer-overlay.component.scss',
})
export class EditCustomerOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly customersFacade = inject(CustomersFacade);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  readonly customer = input.required<Customer>();

  readonly isSubmitting = this.customersFacade.submitting;
  readonly serverError = this.customersFacade.errorMessage;

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly stateOptions = [...GST_STATE_OPTIONS];

  readonly form = this.formBuilder.nonNullable.group(
    {
      name: ['', InputValidators.requiredText(180)],
      phoneNumber: ['', InputValidators.phoneNumber()],
      address: ['', InputValidators.optionalText(320)],
      gstin: ['', InputValidators.gstin()],
      stateCode: [''],
      isActive: [true],
      creditLimit: [
        0,
        [
          ...InputValidators.nonNegativeNumber(),
          Validators.max(99999999.99),
          InputValidators.maxFractionDigits(2),
        ],
      ],
    },
    { validators: InputValidators.gstinMatchesState('gstin', 'stateCode') },
  );

  ngOnInit(): void {
    this.customersFacade.clearError();
    this.customersFacade.clearMutationStatus();

    const c = this.customer();
    this.form.patchValue({
      name: c.name,
      phoneNumber: c.phoneNumber,
      address: c.address ?? '',
      isActive: c.isActive,
      creditLimit: c.creditLimit,
      gstin: c.gstin ?? '',
      stateCode: c.stateCode ?? '',
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
    const raw = this.form.getRawValue();
    this.customersFacade.editCustomer(this.customer().customerId, {
      name: raw.name.trim(),
      phoneNumber: raw.phoneNumber.trim(),
      address: this.nullableTrimmed(raw.address),
      isActive: raw.isActive,
      creditLimit: raw.creditLimit,
      // The server keeps a field that is absent or null; an empty string is what clears it.
      gstin: this.nullableTrimmed(raw.gstin)?.toUpperCase() ?? '',
      // A cleared state select leaves null in the control.
      stateCode: raw.stateCode || '',
    });
  }

  private nullableTrimmed(value: string | null): string | null {
    const normalized = (value ?? '').trim();
    return normalized.length > 0 ? normalized : null;
  }
}
