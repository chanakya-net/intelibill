import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { UserShop } from '../../../core/auth/auth.models';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage, type OfflineSalesDeviceSettings } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineSalesDeviceEnablementService } from '../../sales/services/offline-sales-device-enablement.service';
import { ShopDetails } from '../services/shop.service';
import { ShopsActions } from '../state/shops.actions';
import {
  selectSelectedShopDetails,
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsLoadingDetails,
  selectShopsSubmitting,
} from '../state/shops.selectors';
import { ManageShopOverlayComponent } from './manage-shop-overlay.component';

describe('ManageShopOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const isLoadingDetailsSignal = signal(false);
  const errorSignal = signal('');
  const selectedDetailsSignal = signal<ShopDetails | null>({
    shopId: 'shop-1',
    name: 'Main',
    address: '42 MG Road',
    city: 'Bengaluru',
    state: 'Karnataka',
    pincode: '560001',
    contactPerson: 'Chandra',
    mobileNumber: '9876543210',
    gstNumber: '27AAPFU0939F1ZV',
    bankName: 'SBI',
    bankAccountNumber: '123456789012',
    bankAccountType: 'Savings',
    ifscCode: 'SBIN0001234',
    accountHolderName: 'Chandra Kumar',
  });
  const lastMutationTypeSignal = signal<'create' | 'update' | 'update-bank-details' | 'set-default' | null>(null);
  const lastMutationSucceededSignal = signal(false);

  const canReachApiSignal = signal(true);
  const lastVerifiedAtSignal = signal<Date | null>(new Date('2026-05-21T00:00:00.000Z'));

  const networkStatus = {
    isOnline: signal(true),
    canReachApi: canReachApiSignal,
    lastVerifiedAt: lastVerifiedAtSignal,
    isChecking: signal(false),
    checkConnectivity: vi.fn(async () => {}),
  } satisfies Partial<NetworkStatusService>;

  let storedSettings: OfflineSalesDeviceSettings | null = null;
  const offlineDeviceStorage = {
    getOrCreateDeviceId: vi.fn((_shopId: string) => 'device-1'),
    loadSettings: vi.fn((shopId: string) => {
      storedSettings ??= {
        shopId,
        deviceId: 'device-1',
        label: '',
        enabled: false,
        enabledAt: null,
        enabledByUserId: null,
        enabledByUserName: null,
        lastCompleteSnapshotAt: null,
        lastApiVerifiedAt: null,
        lastSnapshotWarningMarker: null,
        lastReservedLease: null,
      };
      return storedSettings;
    }),
    saveSettings: vi.fn((settings: OfflineSalesDeviceSettings) => {
      storedSettings = settings;
    }),
    updateSettings: vi.fn((shopId: string, update: (current: OfflineSalesDeviceSettings) => OfflineSalesDeviceSettings) => {
      const current = offlineDeviceStorage.loadSettings(shopId) as OfflineSalesDeviceSettings;
      const next = update(current);
      offlineDeviceStorage.saveSettings(next);
      return next;
    }),
  } satisfies Partial<OfflineSalesDeviceSettingsStorage>;

  const offlineEnablement = {
    enableForShop: vi.fn(async (shopId: string, label: string) => ({
      ok: true as const,
      settings: {
        ...(offlineDeviceStorage.loadSettings(shopId) as OfflineSalesDeviceSettings),
        label,
        enabled: true,
        enabledAt: '2026-05-21T00:00:00.000Z',
        enabledByUserId: 'user-1',
        enabledByUserName: 'Test User',
        lastCompleteSnapshotAt: '2026-05-21T00:00:00.000Z',
        lastApiVerifiedAt: '2026-05-21T00:00:00.000Z',
        lastReservedLease: {
          leaseId: 'lease-1',
          fiscalYear: '2026-2027',
          remainingCount: 100,
          expiresAt: '2026-06-21T00:00:00.000Z',
        },
      },
      lease: {
        leaseId: 'lease-1',
        shopId,
        deviceId: 'device-1',
        fiscalYear: '2026-2027',
        prefix: 'INV-',
        numberPadding: 6,
        rangeStart: 1,
        rangeEnd: 100,
        nextNumber: 1,
        remainingCount: 100,
        reservedAt: '2026-05-21T00:00:00.000Z',
        expiresAt: '2026-06-21T00:00:00.000Z',
      },
    })),
  } satisfies Partial<OfflineSalesDeviceEnablementService>;

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectShopsSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectShopsLoadingDetails) {
        return isLoadingDetailsSignal;
      }

      if (selector === selectShopsErrorMessage) {
        return errorSignal;
      }

      if (selector === selectSelectedShopDetails) {
        return selectedDetailsSignal;
      }

      if (selector === selectShopsLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectShopsLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      return signal(undefined);
    }),
  };

  const shops: readonly UserShop[] = [
    { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
    { shopId: 'shop-2', shopName: 'Branch', role: 'Manager', isDefault: false, lastUsedAt: null },
    { shopId: 'shop-3', shopName: 'Outlet', role: 'Staff', isDefault: false, lastUsedAt: null },
  ];

  function setup(): { component: ManageShopOverlayComponent; fixture: ReturnType<typeof TestBed.createComponent<ManageShopOverlayComponent>> } {
    TestBed.configureTestingModule({
      imports: [ManageShopOverlayComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: Store, useValue: store },
        { provide: NetworkStatusService, useValue: networkStatus },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: offlineDeviceStorage },
        { provide: OfflineSalesDeviceEnablementService, useValue: offlineEnablement },
      ],
    });

    const fixture = TestBed.createComponent(ManageShopOverlayComponent);
    fixture.componentInstance.shops = shops;
    fixture.detectChanges();

    return { component: fixture.componentInstance, fixture };
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    isLoadingDetailsSignal.set(false);
    errorSignal.set('');
    selectedDetailsSignal.set({
      shopId: 'shop-1',
      name: 'Main',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: 'Chandra',
      mobileNumber: '9876543210',
      gstNumber: '27AAPFU0939F1ZV',
      bankName: 'SBI',
      bankAccountNumber: '123456789012',
      bankAccountType: 'Savings',
      ifscCode: 'SBIN0001234',
      accountHolderName: 'Chandra Kumar',
    });
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
    canReachApiSignal.set(true);
    storedSettings = null;
    (networkStatus.checkConnectivity as ReturnType<typeof vi.fn>).mockClear();
    (offlineEnablement.enableForShop as ReturnType<typeof vi.fn>).mockClear();
    (offlineDeviceStorage.updateSettings as ReturnType<typeof vi.fn>).mockClear();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads details for the first shop on init through store actions', () => {
    const { component } = setup();

    expect(dispatch).toHaveBeenCalledWith(ShopsActions.selectShop({ shopId: 'shop-1' }));
    expect(dispatch).toHaveBeenCalledWith(ShopsActions.loadShopDetailsRequested({ shopId: 'shop-1' }));
    expect(component.form.controls.name.value).toBe('Main');
    expect(component.form.controls.gstNumber.value).toBe('27AAPFU0939F1ZV');
    expect(component.selectedShopRole()).toBe('Owner');
  });

  it('pre-populates bank form from selected shop details', () => {
    const { component } = setup();

    expect(component.bankForm.controls.bankName.value).toBe('SBI');
    expect(component.bankForm.controls.accountNumber.value).toBe('123456789012');
    expect(component.bankForm.controls.accountType.value).toBe('Savings');
    expect(component.bankForm.controls.ifscCode.value).toBe('SBIN0001234');
    expect(component.bankForm.controls.accountHolderName.value).toBe('Chandra Kumar');
  });

  it('pre-populates bank form with empty strings when bank details are null', () => {
    selectedDetailsSignal.set({
      shopId: 'shop-1',
      name: 'Main',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: null,
      mobileNumber: null,
      gstNumber: null,
      bankName: null,
      bankAccountNumber: null,
      bankAccountType: null,
      ifscCode: null,
      accountHolderName: null,
    });

    const { component } = setup();

    expect(component.bankForm.controls.bankName.value).toBe('');
    expect(component.bankForm.controls.accountNumber.value).toBe('');
    expect(component.bankForm.controls.accountType.value).toBe('');
    expect(component.bankForm.controls.ifscCode.value).toBe('');
    expect(component.bankForm.controls.accountHolderName.value).toBe('');
  });

  it('dispatches detail load when selected shop changes', () => {
    const { component } = setup();
    component.form.controls.shopId.setValue('shop-2');

    component.onShopSelectionChange();

    expect(dispatch).toHaveBeenCalledWith(ShopsActions.selectShop({ shopId: 'shop-2' }));
    expect(dispatch).toHaveBeenCalledWith(ShopsActions.loadShopDetailsRequested({ shopId: 'shop-2' }));
    expect(component.selectedShopRole()).toBe('Manager');
  });

  it('does not submit updates when selected shop role is not owner', () => {
    const { component } = setup();

    component.form.controls.shopId.setValue('shop-2');
    component.onShopSelectionChange();
    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.updateShopFailed({
        errorMessage: 'errors.shops.onlyOwnersCanUpdate',
      })
    );
  });

  it('lets a manager continue to offline billing and enable the device', async () => {
    const { component } = setup();

    component.form.controls.shopId.setValue('shop-2');
    component.onShopSelectionChange();
    component.onContinueToOfflineBilling();
    await component.onEnableOfflineBilling();

    expect(component.activeStep()).toBe(3);
    expect(offlineEnablement.enableForShop).toHaveBeenCalledWith('shop-2', '');
    expect(component.offlineDeviceSettings()?.enabled).toBe(true);
  });

  it('dispatches update action with trimmed values and moves to step 2 on success', () => {
    const { component, fixture } = setup();
    component.form.controls.name.setValue('  Updated Shop  ');
    component.form.controls.address.setValue('  10 New Road  ');
    component.form.controls.city.setValue('  Bengaluru  ');
    component.form.controls.state.setValue('  Karnataka  ');
    component.form.controls.pincode.setValue('  560001  ');
    component.form.controls.contactPerson.setValue('   ');
    component.form.controls.mobileNumber.setValue('  ');
    component.form.controls.gstNumber.setValue('');

    component.onSubmit();

    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.updateShopRequested({
        shopId: 'shop-1',
        payload: {
          name: 'Updated Shop',
          address: '10 New Road',
          city: 'Bengaluru',
          state: 'Karnataka',
          pincode: '560001',
          contactPerson: undefined,
          mobileNumber: undefined,
          gstNumber: undefined,
        },
      })
    );

    lastMutationTypeSignal.set('update');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.activeStep()).toBe(2);
  });

  it('dispatches bank details action with trimmed values and moves to step 3 on success', () => {
    const { component, fixture } = setup();

    component.activeStep.set(2);
    component.bankForm.controls.bankName.setValue('  State Bank of India  ');
    component.bankForm.controls.accountNumber.setValue('  987654321012  ');
    component.bankForm.controls.accountType.setValue('Current');
    component.bankForm.controls.ifscCode.setValue('SBIN0005678');
    component.bankForm.controls.accountHolderName.setValue('  Priya Kumar  ');

    component.onSaveBankDetails();

    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.updateShopBankDetailsRequested({
        shopId: 'shop-1',
        payload: {
          bankName: 'State Bank of India',
          accountNumber: '987654321012',
          accountType: 'Current',
          ifscCode: 'SBIN0005678',
          accountHolderName: 'Priya Kumar',
        },
      })
    );

    lastMutationTypeSignal.set('update-bank-details');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.activeStep()).toBe(3);
  });

  it('does not dispatch bank details when role is not owner', () => {
    const { component } = setup();

    component.form.controls.shopId.setValue('shop-2');
    component.onShopSelectionChange();
    component.onSaveBankDetails();

    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.updateShopBankDetailsFailed({
        errorMessage: 'errors.shops.onlyOwnersCanUpdate',
      })
    );
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.updateShopBankDetailsRequested.type })
    );
  });

  it('does not dispatch bank details when ifsc code is invalid', () => {
    const { component } = setup();

    component.bankForm.controls.ifscCode.setValue('INVALID');
    component.onSaveBankDetails();

    expect(component.bankForm.controls.ifscCode.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.updateShopBankDetailsRequested.type })
    );
  });

  it('dispatches bank details with undefined optionals when fields are blank', () => {
    const { component } = setup();

    component.activeStep.set(2);

    component.bankForm.controls.bankName.setValue('   ');
    component.bankForm.controls.accountNumber.setValue('');
    component.bankForm.controls.accountType.setValue('');
    component.bankForm.controls.ifscCode.setValue('');
    component.bankForm.controls.accountHolderName.setValue('  ');

    component.onSaveBankDetails();

    expect(dispatch).toHaveBeenCalledWith(
      ShopsActions.updateShopBankDetailsRequested({
        shopId: 'shop-1',
        payload: {
          bankName: undefined,
          accountNumber: undefined,
          accountType: undefined,
          ifscCode: undefined,
          accountHolderName: undefined,
        },
      })
    );
  });

  it('resets to step 1 when selected shop changes', () => {
    const { component } = setup();
    component.activeStep.set(2);
    component.form.controls.shopId.setValue('shop-2');

    component.onShopSelectionChange();

    expect(component.activeStep()).toBe(1);
  });

  it('skips bank details and moves to step 3', () => {
    const { component } = setup();
    component.activeStep.set(2);

    component.onSkipBankDetails();

    expect(component.activeStep()).toBe(3);
    expect(dispatch).toHaveBeenCalledWith(ShopsActions.clearMutationStatus());
  });

  it('moves one step back when previous is triggered', () => {
    const { component } = setup();

    component.activeStep.set(4);
    component.onPreviousStep();

    expect(component.activeStep()).toBe(3);
  });

  it('moves to clicked previous icon step only', () => {
    const { component } = setup();

    component.activeStep.set(4);
    component.onStepIconClick(2);
    expect(component.activeStep()).toBe(2);

    component.onStepIconClick(4);
    expect(component.activeStep()).toBe(2);
  });

  it('does not move back while loading details', () => {
    const { component } = setup();

    component.activeStep.set(2);
    isLoadingDetailsSignal.set(true);

    component.onPreviousStep();
    component.onStepIconClick(1);

    expect(component.activeStep()).toBe(2);
  });

  it('emits closeRequested when done is clicked', () => {
    const { component } = setup();
    const closeSpy = vi.fn();

    component.closeRequested.subscribe(closeSpy);
    component.onDone();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('reads server error from selector', () => {
    const { component } = setup();
    errorSignal.set('Only shop owners can update shop details.');

    expect(component.serverError()).toBe('Only shop owners can update shop details.');
  });

  it('does not submit when gstNumber is present but invalid', () => {
    const { component } = setup();

    component.form.controls.gstNumber.setValue('ABC');
    component.onSubmit();

    expect(component.form.controls.gstNumber.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.updateShopRequested.type })
    );
  });

  it('hides offline device setup for staff', () => {
    const { component, fixture } = setup();
    component.form.controls.shopId.setValue('shop-3');
    component.onShopSelectionChange();
    component.activeStep.set(3);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#offline-device-label')).toBeNull();
  });

  it('disables enable button when API is unreachable', () => {
    const { component, fixture } = setup();
    component.activeStep.set(3);
    canReachApiSignal.set(false);
    fixture.detectChanges();

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button.save-button')) as HTMLButtonElement[];
    const enableButton = buttons.find((b) => (b.textContent ?? '').toLowerCase().includes('enable'));
    expect(enableButton).toBeTruthy();
    expect(enableButton!.disabled).toBe(true);
  });

  it('runs enablement and stores returned settings on success', async () => {
    const { component } = setup();
    component.activeStep.set(3);

    await component.onEnableOfflineBilling();

    expect(offlineEnablement.enableForShop).toHaveBeenCalledWith('shop-1', '');
    expect(component.offlineDeviceSettings()?.enabled).toBe(true);
  });

  it('keeps readiness enabled when API becomes unreachable', () => {
    const { component } = setup();
    storedSettings = {
      ...(offlineDeviceStorage.loadSettings('shop-1') as OfflineSalesDeviceSettings),
      enabled: true,
      lastCompleteSnapshotAt: '2026-05-21T00:00:00.000Z',
      lastReservedLease: {
        leaseId: 'lease-1',
        fiscalYear: '2026-2027',
        remainingCount: 100,
        expiresAt: '2026-06-21T00:00:00.000Z',
      },
    };
    component.offlineDeviceSettings.set(storedSettings);
    canReachApiSignal.set(false);

    expect(component.offlineReadinessState()).toBe('enabled');
  });
});
