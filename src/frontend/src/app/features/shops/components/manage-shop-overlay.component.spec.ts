import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';
import { UserShop } from '../../../core/auth/auth.models';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage, type OfflineSalesDeviceSettings } from '../../../core/storage/offline-sales-device-settings.storage';
import { OfflineSalesDeviceEnablementService } from '../../sales/services/offline-sales-device-enablement.service';
import { ShopDetailsDto } from '../services/shop.service';
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
  const lastMutationTypeSignal = signal<'create' | 'update' | 'update-bank-details' | 'set-default' | null>(null);
  const lastMutationSucceededSignal = signal(false);
  const canReachApiSignal = signal(true);
  const selectedDetailsSignal = signal<ShopDetailsDto | null>(baseDetails());

  const networkStatus = {
    isOnline: signal(true),
    canReachApi: canReachApiSignal,
    lastVerifiedAt: signal<Date | null>(new Date('2026-05-21T00:00:00.000Z')),
    isChecking: signal(false),
    checkConnectivity: vi.fn(async () => {}),
  } satisfies Partial<NetworkStatusService>;

  let storedSettings: OfflineSalesDeviceSettings | null = null;
  const offlineDeviceStorage = {
    getOrCreateDeviceId: vi.fn(() => 'device-1'),
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
      const next = update(offlineDeviceStorage.loadSettings(shopId) as OfflineSalesDeviceSettings);
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
        lastReservedLease: { leaseId: 'lease-1', fiscalYear: '2026-2027', remainingCount: 100, expiresAt: '2026-06-21T00:00:00.000Z' },
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
      if (selector === selectShopsSubmitting) return isSubmittingSignal;
      if (selector === selectShopsLoadingDetails) return isLoadingDetailsSignal;
      if (selector === selectShopsErrorMessage) return errorSignal;
      if (selector === selectSelectedShopDetails) return selectedDetailsSignal;
      if (selector === selectShopsLastMutationType) return lastMutationTypeSignal;
      if (selector === selectShopsLastMutationSucceeded) return lastMutationSucceededSignal;
      return signal(undefined);
    }),
  };

  const shops: readonly UserShop[] = [
    { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
    { shopId: 'shop-2', shopName: 'Branch', role: 'Manager', isDefault: false, lastUsedAt: null },
    { shopId: 'shop-3', shopName: 'Outlet', role: 'Staff', isDefault: false, lastUsedAt: null },
  ];

  function setup() {
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

  function baseDetails(): ShopDetailsDto {
    return {
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
    };
  }

  function expectDispatched(action: unknown): void {
    expect(dispatch.mock.calls.map(([call]) => normalizeAction(call))).toContainEqual(normalizeAction(action));
  }

  function normalizeAction<T>(value: T): T {
    if (Array.isArray(value)) return value.map((entry) => normalizeAction(entry)) as T;
    if (value && typeof value === 'object') {
      return Object.fromEntries(Object.entries(value as Record<string, unknown>).filter(([, entry]) => entry !== undefined).map(([key, entry]) => [key, normalizeAction(entry)])) as T;
    }
    return value;
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    isLoadingDetailsSignal.set(false);
    errorSignal.set('');
    selectedDetailsSignal.set(baseDetails());
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
    canReachApiSignal.set(true);
    storedSettings = null;
    (networkStatus.checkConnectivity as ReturnType<typeof vi.fn>).mockClear();
    (offlineEnablement.enableForShop as ReturnType<typeof vi.fn>).mockClear();
    (offlineDeviceStorage.updateSettings as ReturnType<typeof vi.fn>).mockClear();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('loads the first shop and patches forms', () => {
    const { component } = setup();
    expectDispatched(ShopsActions.selectShop({ shopId: 'shop-1' }));
    expectDispatched(ShopsActions.loadShopDetailsRequested({ shopId: 'shop-1' }));
    expect(component.form.controls.name.value).toBe('Main');
    expect(component.bankForm.controls.ifscCode.value).toBe('SBIN0001234');
  });

  it('reloads details when the selected shop changes', () => {
    const { component } = setup();
    component.form.controls.shopId.setValue('shop-2');
    component.onShopSelectionChange();
    expectDispatched(ShopsActions.selectShop({ shopId: 'shop-2' }));
    expectDispatched(ShopsActions.loadShopDetailsRequested({ shopId: 'shop-2' }));
    expect(component.selectedShopRole()).toBe('Manager');
  });

  it('blocks updates for non-owners', () => {
    const { component } = setup();
    component.form.controls.shopId.setValue('shop-2');
    component.onShopSelectionChange();
    component.onSubmit();
    expectDispatched(ShopsActions.updateShopFailed({ errorMessage: 'errors.shops.onlyOwnersCanUpdate' }));
  });

  it('submits shop updates and advances to step 2', () => {
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
    expectDispatched(
      ShopsActions.updateShopRequested({
        shopId: 'shop-1',
        payload: { name: 'Updated Shop', address: '10 New Road', city: 'Bengaluru', state: 'Karnataka', pincode: '560001', contactPerson: undefined, mobileNumber: undefined, gstNumber: undefined },
      })
    );
    lastMutationTypeSignal.set('update');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();
    expect(component.activeStep()).toBe(2);
  });

  it('saves bank details and advances to step 3', () => {
    const { component, fixture } = setup();
    component.activeStep.set(2);
    component.bankForm.controls.bankName.setValue('  State Bank of India  ');
    component.bankForm.controls.accountNumber.setValue('  987654321012  ');
    component.bankForm.controls.accountType.setValue('Current');
    component.bankForm.controls.ifscCode.setValue('SBIN0005678');
    component.bankForm.controls.accountHolderName.setValue('  Priya Kumar  ');
    component.onSaveBankDetails();
    expectDispatched(
      ShopsActions.updateShopBankDetailsRequested({
        shopId: 'shop-1',
        payload: { bankName: 'State Bank of India', accountNumber: '987654321012', accountType: 'Current', ifscCode: 'SBIN0005678', accountHolderName: 'Priya Kumar' },
      })
    );
    lastMutationTypeSignal.set('update-bank-details');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();
    expect(component.activeStep()).toBe(3);
  });

  it('navigates step changes and close action', () => {
    const { component } = setup();
    component.activeStep.set(4);
    component.onPreviousStep();
    expect(component.activeStep()).toBe(3);
    component.onStepIconClick(2);
    expect(component.activeStep()).toBe(2);
    component.onSkipBankDetails();
    expect(component.activeStep()).toBe(3);
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);
    component.onDone();
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('hides offline setup for staff and disables enablement when unreachable', () => {
    const { component, fixture } = setup();
    component.form.controls.shopId.setValue('shop-3');
    component.onShopSelectionChange();
    component.activeStep.set(3);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#offline-device-label')).toBeNull();
    component.form.controls.shopId.setValue('shop-2');
    component.onShopSelectionChange();
    component.activeStep.set(3);
    canReachApiSignal.set(false);
    fixture.detectChanges();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button.save-button')) as HTMLButtonElement[];
    const enableButton = buttons.find((button) => (button.textContent ?? '').toLowerCase().includes('enable'));
    expect(enableButton?.disabled).toBe(true);
  });

  it('runs offline enablement and keeps readiness enabled', async () => {
    const { component } = setup();
    component.activeStep.set(3);
    await component.onEnableOfflineBilling();
    expect(offlineEnablement.enableForShop).toHaveBeenCalledWith('shop-1', '');
    expect(component.offlineDeviceSettings()?.enabled).toBe(true);
    storedSettings = { ...(offlineDeviceStorage.loadSettings('shop-1') as OfflineSalesDeviceSettings), enabled: true, lastCompleteSnapshotAt: '2026-05-21T00:00:00.000Z', lastReservedLease: { leaseId: 'lease-1', fiscalYear: '2026-2027', remainingCount: 100, expiresAt: '2026-06-21T00:00:00.000Z' } };
    component.offlineDeviceSettings.set(storedSettings);
    canReachApiSignal.set(false);
    expect(component.offlineReadinessState()).toBe('enabled');
  });

});
