import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { CreateShopOverlayComponent } from './create-shop-overlay.component';
import { ShopService } from '../services/shop.service';

describe('CreateShopOverlayComponent', () => {
  const shopService = {
    createShop: vi.fn<ShopService['createShop']>(),
  };

  function setup(): CreateShopOverlayComponent {
    TestBed.configureTestingModule({
      imports: [CreateShopOverlayComponent],
      providers: [{ provide: ShopService, useValue: shopService }],
    });

    const fixture = TestBed.createComponent(CreateShopOverlayComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    shopService.createShop.mockReset();
    shopService.createShop.mockReturnValue(of(void 0));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when required fields are missing', () => {
    const component = setup();

    component.form.controls.name.setValue('');
    component.form.controls.address.setValue('');
    component.form.controls.city.setValue('');
    component.form.controls.state.setValue('');
    component.form.controls.pincode.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(shopService.createShop).not.toHaveBeenCalled();
  });

  it('submits trimmed values and omits blank optional fields', () => {
    const component = setup();

    component.form.controls.name.setValue('  Main Shop  ');
    component.form.controls.address.setValue('  42 MG Road  ');
    component.form.controls.city.setValue('  Bengaluru  ');
    component.form.controls.state.setValue('  Karnataka  ');
    component.form.controls.pincode.setValue('  560001  ');
    component.form.controls.contactPerson.setValue('   ');
    component.form.controls.mobileNumber.setValue('  ');

    component.onSubmit();

    expect(shopService.createShop).toHaveBeenCalledWith({
      name: 'Main Shop',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pincode: '560001',
      contactPerson: undefined,
      mobileNumber: undefined,
    });
  });

  it('maps server validation errors into friendly message', () => {
    shopService.createShop.mockReturnValue(
      throwError(() => ({ error: { title: 'Shop.AddressRequired' } }))
    );
    const component = setup();

    component.form.controls.name.setValue('Main Shop');
    component.form.controls.address.setValue('42 MG Road');
    component.form.controls.city.setValue('Bengaluru');
    component.form.controls.state.setValue('Karnataka');
    component.form.controls.pincode.setValue('560001');

    component.onSubmit();

    expect(component.serverError()).toBe('Shop address is required.');
  });

  it('emits closeRequested when close is clicked', () => {
    const component = setup();
    const closeSpy = vi.fn();

    component.closeRequested.subscribe(closeSpy);
    component.onClose();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });
});
