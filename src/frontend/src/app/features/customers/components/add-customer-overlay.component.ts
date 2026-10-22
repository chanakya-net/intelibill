import { Component, EventEmitter, OnInit, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { CustomersFacade } from '../state/customers.facade';
import { InputValidators } from '../../../shared/forms/input-validation';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
} from '../../../shared/primeng-pt.config';

@Component({
  selector: 'app-add-customer-overlay',
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
    TranslocoPipe,
  ],
  templateUrl: './add-customer-overlay.component.html',
  styleUrl: './add-customer-overlay.component.scss',
})
export class AddCustomerOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly customersFacade = inject(CustomersFacade);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  readonly isSubmitting = this.customersFacade.submitting;
  readonly serverError = this.customersFacade.errorMessage;

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', InputValidators.requiredText(180)],
    phoneNumber: ['', InputValidators.phoneNumber()],
    address: ['', InputValidators.optionalText(320)],
    isActive: [true],
    creditLimit: [
      0,
      [
        ...InputValidators.nonNegativeNumber(),
        Validators.max(99999999.99),
        InputValidators.maxFractionDigits(2),
      ],
    ],
  });

  ngOnInit(): void {
    this.customersFacade.clearError();
    this.customersFacade.clearMutationStatus();
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
    this.customersFacade.addCustomer({
      name: raw.name.trim(),
      phoneNumber: raw.phoneNumber.trim(),
      address: this.nullableTrimmed(raw.address),
      isActive: raw.isActive,
      creditLimit: raw.creditLimit,
    });
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
