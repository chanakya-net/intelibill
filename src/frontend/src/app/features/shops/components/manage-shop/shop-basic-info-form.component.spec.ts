import { TestBed } from '@angular/core/testing';

import { ShopDetailsDto } from '../../services/shop.service';
import { ShopBasicInfoFormComponent } from './shop-basic-info-form.component';

describe('ShopBasicInfoFormComponent', () => {
  const initialValues: ShopDetailsDto = {
    shopId: 'shop-1',
    name: 'Main',
    address: '42 MG Road',
    city: 'Bengaluru',
    state: 'Karnataka',
    pincode: '560001',
    contactPerson: 'Chandra',
    mobileNumber: '9876543210',
    gstNumber: '27AAPFU0939F1ZV',
    bankName: null,
    bankAccountNumber: null,
    bankAccountType: null,
    ifscCode: null,
    accountHolderName: null,
  };

  beforeEach(() => TestBed.configureTestingModule({ imports: [ShopBasicInfoFormComponent] }));

  it('patches initial values into the reactive form', () => {
    const fixture = TestBed.createComponent(ShopBasicInfoFormComponent);
    fixture.componentRef.setInput('initialValues', initialValues);
    fixture.detectChanges();

    expect(fixture.componentInstance.form.controls.name.value).toBe('Main');
    expect(fixture.componentInstance.form.controls.city.value).toBe('Bengaluru');
    expect(fixture.componentInstance.form.controls.gstNumber.value).toBe('27AAPFU0939F1ZV');
  });

  it('emits trimmed form values when fields change', () => {
    const fixture = TestBed.createComponent(ShopBasicInfoFormComponent);
    const emitted: unknown[] = [];

    fixture.componentInstance.formChange.subscribe((value) => emitted.push(value));
    fixture.detectChanges();
    fixture.componentInstance.form.controls.name.setValue('  Updated Shop  ');
    fixture.componentInstance.form.controls.contactPerson.setValue('   ');

    expect(emitted.at(-1)).toEqual({
      name: 'Updated Shop',
      address: '',
      city: '',
      state: '',
      pincode: '',
      contactPerson: undefined,
      mobileNumber: undefined,
      gstNumber: undefined,
    });
  });

  it('disables and re-enables the form from input state', () => {
    const fixture = TestBed.createComponent(ShopBasicInfoFormComponent);
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();

    expect(fixture.componentInstance.form.disabled).toBe(true);

    fixture.componentRef.setInput('disabled', false);
    fixture.detectChanges();

    expect(fixture.componentInstance.form.enabled).toBe(true);
  });
});
