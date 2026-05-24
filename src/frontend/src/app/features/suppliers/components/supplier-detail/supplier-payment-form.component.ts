import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import type { MakePaymentRequest } from '../../services/supplier-ledger.service';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
} from '../../../../shared/primeng-pt.config';
import { formatLocalIsoDate } from '../../../../shared/utils/date-time.util';

@Component({
  selector: 'app-supplier-payment-form',
  standalone: true,
  imports: [
    ButtonModule,
    DatePickerModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    ProgressSpinnerModule,
    ReactiveFormsModule,
    TranslocoPipe,
  ],
  templateUrl: './supplier-payment-form.component.html',
  styleUrl: './supplier-payment-form.component.scss',
})
export class SupplierPaymentFormComponent {
  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  @Input() submitting = false;
  @Input() serverError: string | null = null;
  @Input() maxDate = new Date();

  @Output() readonly paymentSubmitted = new EventEmitter<MakePaymentRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  readonly form = new FormGroup({
    amount: new FormControl<number | null>(null, [Validators.required, Validators.min(0.01)]),
    paymentDate: new FormControl<Date>(new Date(), { nonNullable: true, validators: [Validators.required] }),
    notes: new FormControl<string>('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  onCancel(): void {
    if (this.submitting) {
      return;
    }
    this.cancelled.emit();
  }

  onSubmit(): void {
    if (this.submitting) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const paymentDate = this.form.controls.paymentDate.value;

    this.paymentSubmitted.emit({
      amount: this.form.controls.amount.value!,
      paymentDate: formatLocalIsoDate(paymentDate),
      notes: this.nullableTrimmed(this.form.controls.notes.value),
    });
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}

