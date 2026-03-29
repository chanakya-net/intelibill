import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { UserShop } from '../../../core/auth/auth.models';
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
  });
  const lastMutationTypeSignal = signal<'create' | 'update' | 'set-default' | null>(null);
  const lastMutationSucceededSignal = signal(false);

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
  ];

  function setup(): { component: ManageShopOverlayComponent; fixture: ReturnType<typeof TestBed.createComponent<ManageShopOverlayComponent>> } {
    TestBed.configureTestingModule({
      imports: [ManageShopOverlayComponent],
      providers: [{ provide: Store, useValue: store }],
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
    });
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
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
        errorMessage: 'Only shop owners can update shop details.',
      })
    );
  });

  it('dispatches update action with trimmed values and closes on success', () => {
    const { component, fixture } = setup();
    const closeSpy = vi.fn();

    component.closeRequested.subscribe(closeSpy);
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
});
