import { FormControl, FormGroup } from '@angular/forms';

import { InputValidators } from './input-validation';

describe('InputValidators', () => {
  it('rejects required text containing only whitespace', () => {
    const control = new FormControl('   ', InputValidators.requiredText(20));

    expect(control.errors).toEqual({ required: true });
  });

  it('enforces the configured text length', () => {
    const control = new FormControl('too long', InputValidators.requiredText(4));

    expect(control.hasError('maxlength')).toBe(true);
  });

  it.each([
    { value: '+919876543210', valid: true },
    { value: '9876543210', valid: true },
    { value: '123', valid: false },
    { value: 'phone-number', valid: false },
  ])('validates phone number $value', ({ value, valid }) => {
    const control = new FormControl(value, InputValidators.phoneNumber());

    expect(control.valid).toBe(valid);
  });

  it('allows an empty optional phone number', () => {
    const control = new FormControl('', InputValidators.phoneNumber({ required: false }));

    expect(control.valid).toBe(true);
  });

  it.each([
    InputValidators.phoneNumber({ required: false }),
    InputValidators.hsnSacCode(),
    InputValidators.gstNumber(),
    InputValidators.ifscCode(),
  ])('allows whitespace-only optional formatted input', (validators) => {
    const control = new FormControl('   ', validators);

    expect(control.valid).toBe(true);
  });

  it('preserves default limits when validator options are partial', () => {
    const control = new FormControl('a'.repeat(101), InputValidators.password({ required: false }));

    expect(control.hasError('maxlength')).toBe(true);
  });

  it('rejects non-positive numbers', () => {
    const control = new FormControl(0, InputValidators.positiveNumber());

    expect(control.hasError('min')).toBe(true);
  });

  it('rejects percentages outside zero to one hundred', () => {
    const control = new FormControl(101, InputValidators.percentage());

    expect(control.hasError('max')).toBe(true);
  });

  it('rejects numbers with too many fraction digits', () => {
    const control = new FormControl(1.234, InputValidators.maxFractionDigits(2));

    expect(control.errors).toEqual({ maxFractionDigits: { requiredDigits: 2 } });
  });

  it('rejects scientific-notation values with excessive scale', () => {
    const control = new FormControl(1e-7, InputValidators.maxFractionDigits(2));

    expect(control.hasError('maxFractionDigits')).toBe(true);
  });

  it('rejects a positive number that is not an integer', () => {
    const control = new FormControl(1.5, InputValidators.positiveInteger());

    expect(control.hasError('integer')).toBe(true);
  });

  it('marks a form invalid when matching fields differ', () => {
    const form = new FormGroup(
      {
        password: new FormControl('Password1!'),
        confirmation: new FormControl('Different1!'),
      },
      InputValidators.fieldsMatch('password', 'confirmation', 'passwordMismatch'),
    );

    expect(form.errors).toEqual({ passwordMismatch: true });
  });

  it('marks a form invalid when fields that must differ are equal', () => {
    const form = new FormGroup(
      {
        currentPassword: new FormControl('Password1!'),
        newPassword: new FormControl('Password1!'),
      },
      InputValidators.fieldsDiffer('currentPassword', 'newPassword', 'sameAsCurrent'),
    );

    expect(form.errors).toEqual({ sameAsCurrent: true });
  });

  it('marks a form invalid when a bounded amount exceeds its limit field', () => {
    const form = new FormGroup(
      {
        salesPrice: new FormControl(101),
        mrp: new FormControl(100),
      },
      InputValidators.fieldLessThanOrEqual('salesPrice', 'mrp', 'salesPriceExceedsMrp'),
    );

    expect(form.errors).toEqual({ salesPriceExceedsMrp: true });
  });
});
