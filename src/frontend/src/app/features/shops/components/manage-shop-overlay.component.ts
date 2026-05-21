import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettings, OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineSalesDeviceEnablementService } from '../../sales/services/offline-sales-device-enablement.service';
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
  imports: [CommonModule, FormsModule, ReactiveFormsModule, InputTextModule, ButtonModule, ProgressSpinnerModule, SelectModule, StepperModule, TranslocoPipe, BankAccountFormComponent],
  templateUrl: './manage-shop-overlay.component.html',
  styleUrl: './manage-shop-overlay.component.scss',
})
export class ManageShopOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  readonly networkStatus = inject(NetworkStatusService);
  private readonly offlineDeviceSettingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly offlineDeviceEnablement = inject(OfflineSalesDeviceEnablementService);

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
  readonly isOfflineEnablePending = signal(false);
  readonly offlineDeviceSettings = signal<OfflineSalesDeviceSettings | null>(null);
  readonly offlineDeviceLabel = signal('');
  readonly offlineEnablementErrorKey = signal<string>('');

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

    effect(() => {
      const shopId = this.form.controls.shopId.value;
      if (!shopId) {
        this.offlineDeviceSettings.set(null);
        this.offlineDeviceLabel.set('');
        return;
      }

      const settings = this.offlineDeviceSettingsStorage.loadSettings(shopId);
      this.offlineDeviceSettings.set(settings);
      this.offlineDeviceLabel.set(settings?.label ?? '');
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
    void this.networkStatus.checkConnectivity();
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
    this.offlineEnablementErrorKey.set('');
    this.store.dispatch(ShopsActions.selectShop({ shopId }));
    this.store.dispatch(ShopsActions.loadShopDetailsRequested({ shopId }));
    void this.networkStatus.checkConnectivity();
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

  isSelectedShopOwnerOrManager(): boolean {
    const role = this.selectedShopRole().toLowerCase();
    return role === 'owner' || role === 'manager';
  }

  canEnableOfflineBilling(): boolean {
    return this.isSelectedShopOwnerOrManager() && this.networkStatus.canReachApi();
  }

  offlineReadinessState(): 'enabled' | 'needsSetup' | 'apiUnreachable' {
    const shopId = this.form.controls.shopId.value;
    const settings = this.offlineDeviceSettings();
    if (!shopId || !settings?.deviceId) return 'needsSetup';
    if (!this.networkStatus.canReachApi()) return 'apiUnreachable';
    return settings.enabled ? 'enabled' : 'needsSetup';
  }

  offlineSnapshotAgeLabel(): string {
    const completedAt = this.offlineDeviceSettings()?.lastCompleteSnapshotAt;
    if (!completedAt) return '';
    const ms = Date.now() - Date.parse(completedAt);
    if (!Number.isFinite(ms) || ms < 0) return '';
    const minutes = Math.floor(ms / 60000);
    if (minutes < 60) return `${minutes}m`;
    const hours = Math.floor(minutes / 60);
    if (hours < 48) return `${hours}h`;
    const days = Math.floor(hours / 24);
    return `${days}d`;
  }

  offlineInvoiceRemainingCount(): number | null {
    return this.offlineDeviceSettings()?.lastReservedLease?.remainingCount ?? null;
  }

  offlineLastApiVerifiedAt(): string | null {
    return this.offlineDeviceSettings()?.lastApiVerifiedAt ?? null;
  }

  onSaveOfflineDeviceLabel(): void {
    const shopId = this.form.controls.shopId.value;
    if (!shopId) return;
    const next = this.offlineDeviceSettingsStorage.updateSettings(shopId, (current) => ({
      ...current,
      label: this.offlineDeviceLabel().trim(),
    }));
    this.offlineDeviceSettings.set(next);
  }

  async onEnableOfflineBilling(): Promise<void> {
    if (this.isSubmitting() || this.isLoadingDetails() || this.isOfflineEnablePending()) {
      return;
    }

    const shopId = this.form.controls.shopId.value;
    if (!shopId) {
      return;
    }

    if (!this.isSelectedShopOwnerOrManager()) {
      this.offlineEnablementErrorKey.set('offlineSalesDevice.errors.onlyOwnerOrManager');
      return;
    }

    await this.networkStatus.checkConnectivity();
    if (!this.networkStatus.canReachApi()) {
      this.offlineEnablementErrorKey.set('offlineSalesDevice.errors.apiUnreachable');
      return;
    }

    this.isOfflineEnablePending.set(true);
    this.offlineEnablementErrorKey.set('');
    try {
      const result = await this.offlineDeviceEnablement.enableForShop(shopId, this.offlineDeviceLabel().trim());
      if (!result.ok) {
        switch (result.reason) {
          case 'API_UNREACHABLE':
            this.offlineEnablementErrorKey.set('offlineSalesDevice.errors.apiUnreachable');
            break;
          case 'SNAPSHOT_INCOMPLETE':
            this.offlineEnablementErrorKey.set('offlineSalesDevice.errors.snapshotIncomplete');
            break;
          case 'LEASE_UNAVAILABLE':
            this.offlineEnablementErrorKey.set('offlineSalesDevice.errors.leaseUnavailable');
            break;
          default:
            this.offlineEnablementErrorKey.set('offlineSalesDevice.errors.enableFailed');
            break;
        }
        return;
      }

      this.offlineDeviceSettings.set(result.settings);
      this.offlineDeviceLabel.set(result.settings.label);
    } finally {
      this.isOfflineEnablePending.set(false);
    }
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
