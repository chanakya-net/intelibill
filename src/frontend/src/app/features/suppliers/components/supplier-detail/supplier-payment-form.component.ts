import { TillPendingActionsComponent } from '../../../tills/components/till-pending-actions.component';
import { computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ViewChild } from '@angular/core';
import { TillPostingFieldsComponent } from '../../../tills/components/till-posting-fields.component';
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
import {
  PaymentSettlementPreviewService,
  canSubmitPayment,
  shopPaymentDate,
} from '../../../../core/services/payment-settlement-preview.service';
import { LocaleMoneyPipe } from '../../../../shared/pipes/locale-money.pipe';
import {
  parseDateOnlyAsLocalDate,
  formatLocalIsoDate,
} from '../../../../shared/utils/date-time.util';

@Component({
  selector: 'app-supplier-payment-form',
  standalone: true,
  imports: [
    TillPendingActionsComponent,
    TillPostingFieldsComponent,
    ButtonModule,
    DatePickerModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    LocaleMoneyPipe,
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

  readonly targetId = input('');
  readonly submitting = input(false);
  readonly serverError = input<string | null>(null);
  @Input() maxDate = parseDateOnlyAsLocalDate(shopPaymentDate());

  @Output() readonly paymentSubmitted = new EventEmitter<MakePaymentRequest>();
  @Output() readonly cancelled = new EventEmitter<void>();

  @ViewChild(TillPostingFieldsComponent) tillFields!: TillPostingFieldsComponent;
  readonly tillError = signal('');
  readonly tillLocked = signal(false);
  onTillLocked(locked: boolean) {
    this.tillLocked.set(locked);
    if (locked) this.form.disable({ emitEvent: false });
    else this.form.enable({ emitEvent: false });
  }
  readonly form = new FormGroup({
    amount: new FormControl<number | null>(null, [Validators.required, Validators.min(0.01)]),
    paymentDate: new FormControl<Date>(parseDateOnlyAsLocalDate(shopPaymentDate()), {
      nonNullable: true,
      validators: [Validators.required],
    }),
    notes: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
  });

  private readonly amount = toSignal(this.form.controls.amount.valueChanges, {
    initialValue: null,
  });
  private readonly paymentDate = toSignal(this.form.controls.paymentDate.valueChanges, {
    initialValue: this.form.controls.paymentDate.value,
  });
  private readonly refresh = signal(0);
  readonly settlementPreview = inject(PaymentSettlementPreviewService).watch(
    'Supplier',
    this.targetId,
    computed(() => {
      const date = this.paymentDate();
      return date ? formatLocalIsoDate(date) : '';
    }),
    computed(() => ({
      refresh: this.refresh(),
      submitting: this.submitting(),
      error: this.serverError(),
    })),
  );
  readonly settlementLimit = computed(() => {
    const preview = this.settlementPreview();
    return preview.status === 'ready' ? preview.limit : null;
  });
  readonly paymentAllowed = computed(() =>
    canSubmitPayment(this.settlementPreview(), this.amount() ?? 0),
  );

  refreshSettlementPreview(): void {
    this.refresh.update((revision) => revision + 1);
  }

  onCancel(): void {
    if (this.tillLocked() || this.submitting()) {
      return;
    }
    this.cancelled.emit();
  }

  onSubmit(): void {
    if (this.tillLocked() || this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    if (!this.paymentAllowed()) return;

    const paymentDate = this.form.controls.paymentDate.value;

    let tillFields;
    try {
      const { tenderLine: expenseOnlyTender, ...paymentFields } = this.tillFields.fields(
        this.form.controls.amount.value!,
      );
      tillFields = paymentFields;
    } catch {
      this.tillError.set('tills.evidenceRequired');
      return;
    }
    this.paymentSubmitted.emit({
      ...tillFields,
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
