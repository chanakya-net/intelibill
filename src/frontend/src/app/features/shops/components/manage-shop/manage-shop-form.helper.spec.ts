import { FormBuilder } from '@angular/forms';

import { createManageShopForm, mapSetupProfileRequest } from './manage-shop-form.helper';

describe('createManageShopForm', () => {
  it('rejects whitespace-only required shop fields', () => {
    const form = createManageShopForm(new FormBuilder());
    form.patchValue({
      shopId: 'shop-1',
      name: '   ',
      address: '   ',
      city: '   ',
      state: '   ',
      pincode: '   ',
    });

    expect(form.invalid).toBe(true);
    expect(form.controls.name.hasError('required')).toBe(true);
  });
});

it('uses saved protected values while submitting a contact recovery', () => {
  const form = createManageShopForm(new FormBuilder());
  form.patchValue({ name: ' Trade name ', address: 'Stale street', city: 'Stale city', contactPerson: 'Contact' });
  const saved = { name: 'Saved', address: 'Saved street', city: 'Saved city', state: 'Karnataka',
    pincode: '560001', gstNumber: '29ABCDE1234F1Z5', contactPerson: null, mobileNumber: null };
  expect(mapSetupProfileRequest(form, saved, ['name', 'contactPerson', 'mobileNumber'])).toEqual({
    name: 'Trade name', address: 'Saved street', city: 'Saved city', state: 'Karnataka',
    pincode: '560001', gstNumber: '29ABCDE1234F1Z5', contactPerson: 'Contact', mobileNumber: undefined,
  });
});
