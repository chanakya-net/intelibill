import { ExpiryPolicyFormComponent } from '../../expiry/components/expiry-policy-form.component';
import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, Input, OnInit, Output, afterRenderEffect, computed, effect, inject, signal, viewChild } from '@angular/core';
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
import { OfflinePackService } from '../../../core/service-worker/offline-pack.service';
import { LocaleInstantPipe } from '../../../shared/pipes/locale-instant.pipe';
import { UpdateShopRequest, UpdateShopTaxProfileRequest } from '../services/shop.service';
import { ShopsActions } from '../state/shops.actions';
import {
  selectSelectedShopDetails,
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsLoadingDetails,
  selectShopsSubmitting,
} from '../state/shops.selectors';
import { createManageBankForm, createManageShopForm, mapManageBankFormToUpdateBankDetailsRequest, mapManageShopFormToUpdateShopRequest, patchManageBankFormFromDetails, patchManageShopFormFromDetails } from './manage-shop/manage-shop-form.helper';
import { formatOfflinePackVersion, formatOfflineSnapshotAgeLabel, getOfflineInvoiceRemainingCount, getOfflineReadinessState, offlineEnablementErrorKeyForReason, offlineEnablementProgressKey } from './manage-shop/manage-shop-offline.helper';
import { ShopBasicInfoFormComponent } from './manage-shop/shop-basic-info-form.component';
import { ShopLogoUploadComponent } from './manage-shop/shop-logo-upload.component';
import { ShopMembersTableComponent } from './manage-shop/shop-members-table.component';
import { ShopTaxProfileFormComponent } from './manage-shop/shop-tax-profile-form.component';
import { ShopTillSettingsComponent } from './manage-shop/shop-till-settings.component';

@Component({
  selector: 'app-manage-shop-overlay',
  standalone: true,
  imports: [
    CommonModule,
    ExpiryPolicyFormComponent,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    ProgressSpinnerModule,
    StepperModule,
    TranslocoPipe,
    LocaleInstantPipe,
    BankAccountFormComponent,
    ShopBasicInfoFormComponent,
    ShopLogoUploadComponent,
    ShopMembersTableComponent,
    ShopTaxProfileFormComponent,
    ShopTillSettingsComponent,
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
  private readonly offlinePack = inject(OfflinePackService);

  readonly isSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly isLoadingDetails = this.store.selectSignal(selectShopsLoadingDetails);
  readonly serverError = this.store.selectSignal(selectShopsErrorMessage);
  readonly selectedShopDetails = this.store.selectSignal(selectSelectedShopDetails);
  readonly lastMutationType = this.store.selectSignal(selectShopsLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectShopsLastMutationSucceeded);

  readonly selectedShopRole = signal('');
  readonly activeStep = signal(1);
  private readonly stepper = viewChild('stepper', { read: ElementRef<HTMLElement> });
  readonly isUpdatePending = signal(false);
  readonly isBankDetailsPending = signal(false);
  readonly isTaxProfilePending = signal(false);
  /** The tax profile saves on its own, so its outcome is shown in its own section. */
  readonly taxProfileError = computed(() =>
    this.lastMutationType() === 'update-tax-profile' ? this.serverError() : '',
  );
  readonly taxProfileSaved = computed(
    () => this.lastMutationType() === 'update-tax-profile' && this.lastMutationSucceeded(),
  );
  readonly shopFormError = computed(() =>
    this.lastMutationType() === 'update-tax-profile' ? '' : this.serverError(),
  );
  readonly isOfflineEnablePending = signal(false);
  readonly offlineDeviceSettings = signal<OfflineSalesDeviceSettings | null>(null);
  readonly offlineDeviceLabel = signal('');
  readonly offlineEnablementErrorKey = signal('');
  /** The app's files for offline use, shared by every shop on this device. */
  readonly offlinePackStatus = this.offlinePack.status;
  readonly offlinePackVersion = computed(() => formatOfflinePackVersion(this.offlinePackStatus()));
  /** What an enable in progress is doing (downloading the app files, the snapshot, the lease). */
  readonly offlineEnablementProgressKey = computed(() => offlineEnablementProgressKey(this.offlineDeviceEnablement.step()));
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
  /** Set once the owner edits the shop details and cleared when they are saved or another shop is chosen. */
  private hasUnsavedBasicInfo = false;

  roleLabelKey(role: string): string {
    const normalized = role.toLowerCase();
    return ['owner', 'manager', 'staff'].includes(normalized)
      ? `users.${normalized}`
      : 'shops.unknown';
  }

  constructor() {
    afterRenderEffect({
      write: () => {
        // Start each step at the top; ordinary field edits keep their scroll position.
        this.activeStep();
        const stepper = this.stepper()?.nativeElement;
        if (stepper) stepper.scrollTop = 0;
      },
    });
    effect(() => {
      const details = this.selectedShopDetails();
      if (!details || details.shopId !== this.form.controls.shopId.value) return;
      // Details also refresh when the tax profile saves on its own; edits not yet saved in a step stay.
      if (!this.hasUnsavedBasicInfo) patchManageShopFormFromDetails(this.form, details);
      if (this.bankForm.pristine) patchManageBankFormFromDetails(this.bankForm, details);
    });
    effect(() => {
      const ok = this.lastMutationType() === 'update' && this.lastMutationSucceeded();
      if (this.isUpdatePending() && ok && !this.isSubmitting()) {
        this.isUpdatePending.set(false);
        this.hasUnsavedBasicInfo = false;
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
      if (this.isTaxProfilePending() && this.lastMutationType() === 'update-tax-profile' && !this.isSubmitting()) {
        this.isTaxProfilePending.set(false);
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
    this.hasUnsavedBasicInfo = true;
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
    this.hasUnsavedBasicInfo = false;
    this.bankForm.markAsPristine();
    this.store.dispatch(ShopsActions.clearMutationStatus());
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
    // The GSTIN is edited only in the tax profile here, so the shop's current one is sent back unchanged.
    const payload = { ...mapManageShopFormToUpdateShopRequest(this.form), gstNumber: this.selectedShopDetails()?.gstNumber ?? undefined };
    this.store.dispatch(ShopsActions.updateShopRequested({ shopId, payload }));
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

  onSaveTaxProfile(payload: UpdateShopTaxProfileRequest): void {
    if (this.isSubmitting() || this.isLoadingDetails()) return;
    if (!this.isSelectedShopOwner()) {
      this.store.dispatch(ShopsActions.updateShopTaxProfileFailed({ errorMessage: 'errors.shops.onlyOwnersCanUpdate' }));
      return;
    }
    const shopId = this.form.controls.shopId.value;
    if (!shopId) return;
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isTaxProfilePending.set(true);
    this.store.dispatch(ShopsActions.updateShopTaxProfileRequested({ shopId, payload }));
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

  /** Enabled here, but the app's files for offline use were last found incomplete. */
  isOfflinePackIncomplete(): boolean {
    return this.offlineReadinessState() === 'enabled' && this.offlinePackStatus()?.state === 'incomplete';
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
        this.offlineEnablementErrorKey.set(offlineEnablementErrorKeyForReason(result.reason, result.errorCode, result.packFailure));
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
