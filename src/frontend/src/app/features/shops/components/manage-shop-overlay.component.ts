import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { StepperModule } from 'primeng/stepper';

import { BankAccountFormComponent } from '../../../shared/components/bank-account-form/bank-account-form.component';
import { UserShop } from '../../../core/auth/auth.models';
import { RootState } from '../../../core/state/app.state';
import { CreateShopRequest, UpdateBankDetailsRequest } from '../services/shop.service';
import { ShopsActions } from '../state/shops.actions';
import {
  selectSelectedShopDetails,
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsLoadingDetails,
  selectShopsSubmitting,
} from '../state/shops.selectors';

const INDIA_GST_REGEX = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/i;
const INDIA_IFSC_REGEX = /^[A-Z]{4}0[A-Z0-9]{6}$/i;

@Component({
  selector: 'app-manage-shop-overlay',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, ProgressSpinnerModule, SelectModule, StepperModule, TranslocoPipe, BankAccountFormComponent],
  templateUrl: './manage-shop-overlay.component.html',
  styleUrl: './manage-shop-overlay.component.scss',
})
export class ManageShopOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly isLoadingDetails = this.store.selectSignal(selectShopsLoadingDetails);
  readonly serverError = this.store.selectSignal(selectShopsErrorMessage);
  readonly selectedShopDetails = this.store.selectSignal(selectSelectedShopDetails);
  readonly lastMutationType = this.store.selectSignal(selectShopsLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectShopsLastMutationSucceeded);

  readonly selectedShopRole = signal<string>('');
  readonly activeStep = signal(1);
  readonly isUpdatePending = signal(false);
  readonly isBankDetailsPending = signal(false);

  @Input({ required: true }) shops: readonly UserShop[] = [];
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    shopId: ['', [Validators.required]],
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

  readonly progressSpinnerPt = {
    root: { class: 'manage-shop-spinner-root' },
  };

  constructor() {
    effect(() => {
      const details = this.selectedShopDetails();
      const selectedShopId = this.form.controls.shopId.value;
      if (!details || details.shopId !== selectedShopId) {
        return;
      }

      this.form.patchValue({
        name: details.name,
        address: details.address,
        city: details.city,
        state: details.state,
        pincode: details.pincode,
        contactPerson: details.contactPerson ?? '',
        mobileNumber: details.mobileNumber ?? '',
        gstNumber: details.gstNumber ?? '',
      });

      this.bankForm.patchValue({
        bankName: details.bankName ?? '',
        accountNumber: details.bankAccountNumber ?? '',
        accountType: details.bankAccountType ?? '',
        ifscCode: details.ifscCode ?? '',
        accountHolderName: details.accountHolderName ?? '',
      });
    });

    effect(() => {
      const isUpdateSuccess = this.lastMutationType() === 'update' && this.lastMutationSucceeded();
      if (!this.isUpdatePending() || !isUpdateSuccess || this.isSubmitting()) {
        return;
      }

      this.isUpdatePending.set(false);
      this.store.dispatch(ShopsActions.clearMutationStatus());
      this.activeStep.set(2);
    });

    effect(() => {
      const isBankSuccess = this.lastMutationType() === 'update-bank-details' && this.lastMutationSucceeded();
      if (!this.isBankDetailsPending() || !isBankSuccess || this.isSubmitting()) {
        return;
      }

      this.isBankDetailsPending.set(false);
      this.store.dispatch(ShopsActions.clearMutationStatus());
      this.activeStep.set(3);
    });
  }

  ngOnInit(): void {
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());

    if (this.shops.length === 0) {
      return;
    }

    const initialShopId = this.shops[0].shopId;
    this.form.controls.shopId.setValue(initialShopId);
    this.updateSelectedRole(initialShopId);
    this.store.dispatch(ShopsActions.selectShop({ shopId: initialShopId }));
    this.store.dispatch(ShopsActions.loadShopDetailsRequested({ shopId: initialShopId }));
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.closeRequested.emit();
  }

  onShopSelectionChange(): void {
    const shopId = this.form.controls.shopId.value;
    if (!shopId) {
      return;
    }

    this.updateSelectedRole(shopId);
    this.activeStep.set(1);
    this.store.dispatch(ShopsActions.selectShop({ shopId }));
    this.store.dispatch(ShopsActions.loadShopDetailsRequested({ shopId }));
  }

  onSubmit(): void {
    if (this.isSubmitting() || this.isLoadingDetails()) {
      return;
    }

    if (!this.isSelectedShopOwner()) {
        this.store.dispatch(
          ShopsActions.updateShopFailed({
            errorMessage: 'errors.shops.onlyOwnersCanUpdate',
          })
        );
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const shopId = this.form.controls.shopId.value;
    if (!shopId) {
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isUpdatePending.set(true);

    const payload: CreateShopRequest = {
      name: this.form.controls.name.value.trim(),
      address: this.form.controls.address.value.trim(),
      city: this.form.controls.city.value.trim(),
      state: this.form.controls.state.value.trim(),
      pincode: this.form.controls.pincode.value.trim(),
      contactPerson: this.toOptionalValue(this.form.controls.contactPerson.value),
      mobileNumber: this.toOptionalValue(this.form.controls.mobileNumber.value),
      gstNumber: this.toOptionalValue(this.form.controls.gstNumber.value),
    };

    this.store.dispatch(ShopsActions.updateShopRequested({ shopId, payload }));
  }

  onSaveBankDetails(): void {
    if (this.isSubmitting() || this.isLoadingDetails()) {
      return;
    }

    if (!this.isSelectedShopOwner()) {
      this.store.dispatch(
        ShopsActions.updateShopBankDetailsFailed({
          errorMessage: 'errors.shops.onlyOwnersCanUpdate',
        })
      );
      return;
    }

    if (this.bankForm.invalid) {
      this.bankForm.markAllAsTouched();
      return;
    }

    const shopId = this.form.controls.shopId.value;
    if (!shopId) {
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isBankDetailsPending.set(true);

    const payload: UpdateBankDetailsRequest = {
      bankName: this.toOptionalValue(this.bankForm.controls.bankName.value),
      accountNumber: this.toOptionalValue(this.bankForm.controls.accountNumber.value),
      accountType: this.toOptionalValue(this.bankForm.controls.accountType.value),
      ifscCode: this.toOptionalValue(this.bankForm.controls.ifscCode.value),
      accountHolderName: this.toOptionalValue(this.bankForm.controls.accountHolderName.value),
    };

    this.store.dispatch(ShopsActions.updateShopBankDetailsRequested({ shopId, payload }));
  }

  onSkipBankDetails(): void {
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.activeStep.set(3);
  }

  onPreviousStep(): void {
    if (this.isSubmitting() || this.isLoadingDetails()) {
      return;
    }

    const previousStep = this.activeStep() - 1;
    if (previousStep < 1) {
      return;
    }

    this.activeStep.set(previousStep);
  }

  onStepIconClick(targetStep: number): void {
    if (this.isSubmitting() || this.isLoadingDetails()) {
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

  isSelectedShopOwner(): boolean {
    return this.selectedShopRole().toLowerCase() === 'owner';
  }

  private updateSelectedRole(shopId: string): void {
    const selected = this.shops.find((shop) => shop.shopId === shopId);
    this.selectedShopRole.set(selected?.role ?? '');
  }

  private toOptionalValue(value: string): string | undefined {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : undefined;
  }
}
