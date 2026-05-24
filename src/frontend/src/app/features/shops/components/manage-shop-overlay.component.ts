import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, effect, inject, signal } from '@angular/core';
import { FormsModule, ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { StepperModule } from 'primeng/stepper';

import { UserShop } from '../../../core/auth/auth.models';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { RootState } from '../../../core/state/app.state';
import { OfflineSalesDeviceSettings, OfflineSalesDeviceSettingsStorage } from '../../../core/storage/offline-sales-device-settings.storage';
import { BankAccountFormComponent } from '../../../shared/components/bank-account-form/bank-account-form.component';
import { OfflineSalesDeviceEnablementService } from '../../sales/services/offline-sales-device-enablement.service';
import { UpdateShopRequest } from '../services/shop.service';
import { ShopsActions } from '../state/shops.actions';
import {
  selectSelectedShopDetails,
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsLoadingDetails,
  selectShopsSubmitting,
} from '../state/shops.selectors';
import { createManageBankForm, createManageShopForm, mapManageBankFormToUpdateBankDetailsRequest, mapManageShopFormToUpdateShopRequest, patchManageShopFormsFromDetails } from './manage-shop/manage-shop-form.helper';
import { formatOfflineSnapshotAgeLabel, getOfflineInvoiceRemainingCount, getOfflineReadinessState, offlineEnablementErrorKeyForReason } from './manage-shop/manage-shop-offline.helper';
import { ShopBasicInfoFormComponent } from './manage-shop/shop-basic-info-form.component';
import { ShopLogoUploadComponent } from './manage-shop/shop-logo-upload.component';
import { ShopMembersTableComponent } from './manage-shop/shop-members-table.component';

@Component({
  selector: 'app-manage-shop-overlay',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    ProgressSpinnerModule,
    StepperModule,
    TranslocoPipe,
    BankAccountFormComponent,
    ShopBasicInfoFormComponent,
    ShopLogoUploadComponent,
    ShopMembersTableComponent,
  ],
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

  readonly selectedShopRole = signal('');
  readonly activeStep = signal(1);
  readonly isUpdatePending = signal(false);
  readonly isBankDetailsPending = signal(false);
  readonly isOfflineEnablePending = signal(false);
  readonly offlineDeviceSettings = signal<OfflineSalesDeviceSettings | null>(null);
  readonly offlineDeviceLabel = signal('');
  readonly offlineEnablementErrorKey = signal('');
  readonly stepIcons = [
    { value: 1, icon: 'pi pi-shop' },
    { value: 2, icon: 'pi pi-credit-card' },
    { value: 3, icon: 'pi pi-wifi' },
    { value: 4, icon: 'pi pi-check' },
  ] as const;

  @Input({ required: true }) shops: readonly UserShop[] = [];
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = createManageShopForm(this.formBuilder);
  readonly bankForm = createManageBankForm(this.formBuilder);
  readonly progressSpinnerPt = { root: { class: 'manage-shop-spinner-root' } };

  constructor() {
    effect(() => {
      const details = this.selectedShopDetails();
      if (details && details.shopId === this.form.controls.shopId.value) patchManageShopFormsFromDetails(this.form, this.bankForm, details);
    });
    effect(() => {
      const ok = this.lastMutationType() === 'update' && this.lastMutationSucceeded();
      if (this.isUpdatePending() && ok && !this.isSubmitting()) {
        this.isUpdatePending.set(false);
        this.store.dispatch(ShopsActions.clearMutationStatus());
        this.activeStep.set(2);
      }
    });
    effect(() => {
      const ok = this.lastMutationType() === 'update-bank-details' && this.lastMutationSucceeded();
      if (this.isBankDetailsPending() && ok && !this.isSubmitting()) {
        this.isBankDetailsPending.set(false);
        this.store.dispatch(ShopsActions.clearMutationStatus());
        this.activeStep.set(3);
      }
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
    if (!this.shops.length) return;
    const initialShopId = this.shops[0].shopId;
    this.form.controls.shopId.setValue(initialShopId);
    this.updateSelectedRole(initialShopId);
    this.store.dispatch(ShopsActions.selectShop({ shopId: initialShopId }));
    this.store.dispatch(ShopsActions.loadShopDetailsRequested({ shopId: initialShopId }));
    void this.networkStatus.checkConnectivity();
  }

  onClose(): void {
    if (!this.isSubmitting()) this.closeRequested.emit();
  }

  onBasicInfoFormChange(payload: Partial<UpdateShopRequest>): void {
    this.form.patchValue({
      name: payload.name ?? '',
      address: payload.address ?? '',
      city: payload.city ?? '',
      state: payload.state ?? '',
      pincode: payload.pincode ?? '',
      contactPerson: payload.contactPerson ?? '',
      mobileNumber: payload.mobileNumber ?? '',
      gstNumber: payload.gstNumber ?? '',
    });
  }

  onShopSelectionChange(): void {
    const shopId = this.form.controls.shopId.value;
    if (!shopId) return;
    this.updateSelectedRole(shopId);
    this.activeStep.set(1);
    this.offlineEnablementErrorKey.set('');
    this.store.dispatch(ShopsActions.selectShop({ shopId }));
    this.store.dispatch(ShopsActions.loadShopDetailsRequested({ shopId }));
    void this.networkStatus.checkConnectivity();
  }

  onContinueToOfflineBilling(): void {
    if (this.isSubmitting() || this.isLoadingDetails() || !this.isSelectedShopOwnerOrManager()) return;
    this.offlineEnablementErrorKey.set('');
    this.activeStep.set(3);
  }

  onSubmit(): void {
    if (this.isSubmitting() || this.isLoadingDetails()) return;
    if (!this.isSelectedShopOwner()) {
      this.store.dispatch(ShopsActions.updateShopFailed({ errorMessage: 'errors.shops.onlyOwnersCanUpdate' }));
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const shopId = this.form.controls.shopId.value;
    if (!shopId) return;
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isUpdatePending.set(true);
    this.store.dispatch(ShopsActions.updateShopRequested({ shopId, payload: mapManageShopFormToUpdateShopRequest(this.form) }));
  }

  onSaveBankDetails(): void {
    if (this.isSubmitting() || this.isLoadingDetails()) return;
    if (!this.isSelectedShopOwner()) {
      this.store.dispatch(ShopsActions.updateShopBankDetailsFailed({ errorMessage: 'errors.shops.onlyOwnersCanUpdate' }));
      return;
    }
    if (this.bankForm.invalid) {
      this.bankForm.markAllAsTouched();
      return;
    }
    const shopId = this.form.controls.shopId.value;
    if (!shopId) return;
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isBankDetailsPending.set(true);
    this.store.dispatch(ShopsActions.updateShopBankDetailsRequested({ shopId, payload: mapManageBankFormToUpdateBankDetailsRequest(this.bankForm) }));
  }

  onSkipBankDetails(): void {
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.activeStep.set(3);
  }

  onPreviousStep(): void {
    if (this.isSubmitting() || this.isLoadingDetails()) return;
    const previousStep = this.activeStep() - 1;
    if (previousStep >= 1) this.activeStep.set(previousStep);
  }

  onStepIconClick(targetStep: number): void {
    if (this.isSubmitting() || this.isLoadingDetails() || targetStep >= this.activeStep() || targetStep < 1) return;
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

  offlineReadinessState(): 'enabled' | 'needsSetup' {
    return getOfflineReadinessState(this.form.controls.shopId.value, this.offlineDeviceSettings());
  }

  offlineSnapshotAgeLabel(): string {
    return formatOfflineSnapshotAgeLabel(this.offlineDeviceSettings()?.lastCompleteSnapshotAt);
  }

  offlineInvoiceRemainingCount(): number | null {
    return getOfflineInvoiceRemainingCount(this.offlineDeviceSettings());
  }

  onSaveOfflineDeviceLabel(): void {
    const shopId = this.form.controls.shopId.value;
    if (!shopId) return;
    const next = this.offlineDeviceSettingsStorage.updateSettings(shopId, (current) => ({ ...current, label: this.offlineDeviceLabel().trim() }));
    this.offlineDeviceSettings.set(next);
  }

  async onEnableOfflineBilling(): Promise<void> {
    if (this.isSubmitting() || this.isLoadingDetails() || this.isOfflineEnablePending()) return;
    const shopId = this.form.controls.shopId.value;
    if (!shopId) return;
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
        this.offlineEnablementErrorKey.set(offlineEnablementErrorKeyForReason(result.reason));
        return;
      }
      this.offlineDeviceSettings.set(result.settings);
      this.offlineDeviceLabel.set(result.settings.label);
    } finally {
      this.isOfflineEnablePending.set(false);
    }
  }

  private updateSelectedRole(shopId: string): void {
    this.selectedShopRole.set(this.shops.find((shop) => shop.shopId === shopId)?.role ?? '');
  }
}
