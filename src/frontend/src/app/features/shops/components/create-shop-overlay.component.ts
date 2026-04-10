import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnInit, Output, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { StepperModule } from 'primeng/stepper';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { ShopsActions } from '../state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsSubmitting,
} from '../state/shops.selectors';

const INDIA_GST_REGEX = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/i;
const INDIA_IFSC_REGEX = /^[A-Z]{4}0[A-Z0-9]{6}$/i;

@Component({
  selector: 'app-create-shop-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
    SelectModule,
    StepperModule,
    TranslocoPipe,
  ],
  templateUrl: './create-shop-overlay.component.html',
  styleUrl: './create-shop-overlay.component.scss',
})
export class CreateShopOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly isSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly serverError = this.store.selectSignal(selectShopsErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectShopsLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectShopsLastMutationSucceeded);

  readonly activeStep = signal(1);
  readonly isCreatePending = signal(false);
  readonly isBankDetailsPending = signal(false);

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly shopForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    address: ['', [Validators.required, Validators.maxLength(320)]],
    city: ['', [Validators.required, Validators.maxLength(120)]],
    state: ['', [Validators.required, Validators.maxLength(120)]],
    pincode: ['', [Validators.required, Validators.maxLength(16)]],
    contactPerson: ['', [Validators.maxLength(120)]],
    mobileNumber: ['', [Validators.maxLength(32)]],
    gstNumber: ['', [Validators.maxLength(20), Validators.pattern(INDIA_GST_REGEX)]],
  });

  readonly bankForm = this.formBuilder.nonNullable.group({
    bankName: ['', [Validators.maxLength(120)]],
    accountNumber: ['', [Validators.maxLength(50)]],
    accountType: [''],
    ifscCode: ['', [Validators.maxLength(20), Validators.pattern(INDIA_IFSC_REGEX)]],
    accountHolderName: ['', [Validators.maxLength(120)]],
  });

  readonly accountTypeOptions = [
    { label: 'Savings', value: 'Savings' },
    { label: 'Current', value: 'Current' },
  ];

  readonly progressSpinnerPt = {
    root: { class: 'create-shop-spinner-root' },
  };

  constructor() {
    effect(() => {
      const mutationType = this.lastMutationType();
      const succeeded = this.lastMutationSucceeded();
      const submitting = this.isSubmitting();

      if (submitting) return;

      if (mutationType === 'create' && this.isCreatePending() && succeeded) {
        this.isCreatePending.set(false);
        this.store.dispatch(ShopsActions.clearMutationStatus());
        this.activeStep.set(2);
      }

      if (mutationType === 'update-bank-details' && this.isBankDetailsPending() && succeeded) {
        this.isBankDetailsPending.set(false);
        this.store.dispatch(ShopsActions.clearMutationStatus());
        this.activeStep.set(3);
      }
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

  onNextFromStep1(): void {
    if (this.isSubmitting()) return;

    if (this.shopForm.invalid) {
      this.shopForm.markAllAsTouched();
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isCreatePending.set(true);

    const payload = {
      name: this.shopForm.controls.name.value.trim(),
      address: this.shopForm.controls.address.value.trim(),
      city: this.shopForm.controls.city.value.trim(),
      state: this.shopForm.controls.state.value.trim(),
      pincode: this.shopForm.controls.pincode.value.trim(),
      contactPerson: this.toOptionalValue(this.shopForm.controls.contactPerson.value),
      mobileNumber: this.toOptionalValue(this.shopForm.controls.mobileNumber.value),
      gstNumber: this.toOptionalValue(this.shopForm.controls.gstNumber.value),
    };

    this.store.dispatch(ShopsActions.createShopRequested({ payload }));
  }

  onSaveFromStep2(): void {
    if (this.isSubmitting()) return;

    if (this.bankForm.invalid) {
      this.bankForm.markAllAsTouched();
      return;
    }

    const shopId = this.authService.session()?.activeShopId;
    if (!shopId) {
      this.activeStep.set(3);
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isBankDetailsPending.set(true);

    const payload = {
      bankName: this.toOptionalValue(this.bankForm.controls.bankName.value),
      accountNumber: this.toOptionalValue(this.bankForm.controls.accountNumber.value),
      accountType: this.toOptionalValue(this.bankForm.controls.accountType.value) ?? undefined,
      ifscCode: this.toOptionalValue(this.bankForm.controls.ifscCode.value),
      accountHolderName: this.toOptionalValue(this.bankForm.controls.accountHolderName.value),
    };

    this.store.dispatch(ShopsActions.updateShopBankDetailsRequested({ shopId, payload }));
  }

  onSkipStep2(): void {
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.activeStep.set(3);
  }

  onPreviousStep(): void {
    if (this.isSubmitting()) {
      return;
    }

    const previousStep = this.activeStep() - 1;
    if (previousStep < 1) {
      return;
    }

    this.activeStep.set(previousStep);
  }

  onStepIconClick(targetStep: number): void {
    if (this.isSubmitting()) {
      return;
    }

    if (targetStep >= this.activeStep() || targetStep < 1) {
      return;
    }

    this.activeStep.set(targetStep);
  }

  onDone(): void {
    this.closeRequested.emit();
  }

  private toOptionalValue(value: string): string | undefined {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : undefined;
  }
}
