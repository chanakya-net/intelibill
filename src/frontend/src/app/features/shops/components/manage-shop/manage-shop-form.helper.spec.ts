import { FormBuilder } from '@angular/forms';

import { createManageShopForm } from './manage-shop-form.helper';

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
