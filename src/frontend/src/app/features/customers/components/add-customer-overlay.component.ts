import { Component, EventEmitter, OnInit, Output, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
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

import { AuthService } from '../../../core/auth/auth.service';
import { CustomersFacade } from '../state/customers.facade';
import {
  MAX_CREDIT_TERMS_DAYS,
  SELECTABLE_CREDIT_POLICIES,
  creditPolicyLabelKey,
  enforcedPolicyNeedsLimit,
} from '../utils/credit-policy.util';
import { CreditPolicyMode } from '../services/customer.service';
import { InputValidators } from '../../../shared/forms/input-validation';
import { GST_STATE_OPTIONS } from '../../../shared/gst/gst-state-codes';
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
    SelectModule,
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

  readonly stateOptions = [...GST_STATE_OPTIONS];
  readonly creditPolicyOptions = computed(() =>
    SELECTABLE_CREDIT_POLICIES.map((value) => ({
      value,
      label: creditPolicyLabelKey(value),
    })),
  );

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
      creditPolicyMode: ['NoCredit' as CreditPolicyMode, Validators.required],
      creditTermsDays: [null as number | null, [Validators.min(0), Validators.max(MAX_CREDIT_TERMS_DAYS)]],
    },
    {
      validators: [
        InputValidators.gstinMatchesState('gstin', 'stateCode'),
        enforcedPolicyNeedsLimit('creditPolicyMode', 'creditLimit'),
      ],
    },
  );

  readonly maxTermsDays = MAX_CREDIT_TERMS_DAYS;
  private readonly authService = inject(AuthService);
  /** Credit limit, policy and terms are an owner's or manager's decision; the server refuses staff too. */
  readonly canEditCredit = computed(() => {
    const session = this.authService.session();
    const role = session?.shops?.find((shop) => shop.shopId === session.activeShopId)?.role;
    return role === 'Owner' || role === 'Manager';
  });
  private readonly selectedPolicy = toSignal(this.form.controls.creditPolicyMode.valueChanges, {
    initialValue: this.form.controls.creditPolicyMode.value,
  });
  /** Hint for the chosen policy, so a zero limit is never read as "unlimited". */
  readonly creditPolicyHintKey = computed(() => `customers.creditPolicy.hints.${this.selectedPolicy()}`);

  ngOnInit(): void {
    if (!this.canEditCredit()) {
      this.form.controls.creditLimit.disable();
      this.form.controls.creditPolicyMode.disable();
      this.form.controls.creditTermsDays.disable();
    }
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
      creditPolicyMode: raw.creditPolicyMode,
      creditTermsDays: raw.creditTermsDays ?? null,
      gstin: this.nullableTrimmed(raw.gstin)?.toUpperCase() ?? null,
      // A cleared state select leaves null in the control.
      stateCode: raw.stateCode || null,
    });
  }

  private nullableTrimmed(value: string | null): string | null {
    const normalized = (value ?? '').trim();
    return normalized.length > 0 ? normalized : null;
  }
}
