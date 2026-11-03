import { FormBuilder } from '@angular/forms';

import { InputValidators } from '../../../../shared/forms/input-validation';
import {
  createTaxProfileControls,
  taxRegistrationValidator,
  toCreateShopTaxProfileRequest,
} from './shop-tax-profile-form.helper';

describe('shop tax profile form helper', () => {
  function form() {
    return new FormBuilder().nonNullable.group(
      { ...createTaxProfileControls(), gstNumber: ['', InputValidators.gstin()] },
      { validators: taxRegistrationValidator() },
    );
  }

  it('starts undeclared with the default series and requires a state and a regime', () => {
    const group = form();

    expect(group.controls.invoiceSeriesCode.value).toBe('A');
    expect(group.controls.stateCode.hasError('required')).toBe(true);
    expect(group.controls.taxRegime.hasError('required')).toBe(true);
  });

  it('requires a checksum-valid GSTIN from the chosen state for a regular or composition shop', () => {
    const group = form();
    group.patchValue({ stateCode: '27', taxRegime: 'Composition' });
    expect(group.hasError('gstinRequired')).toBe(true);

    group.controls.gstNumber.setValue('27AAPFU0939F1ZW');
    expect(group.controls.gstNumber.hasError('gstinChecksum')).toBe(true);

    group.controls.gstNumber.setValue('29AAGCB7383J1Z4');
    expect(group.hasError('gstinStateMismatch')).toBe(true);

    group.controls.gstNumber.setValue('27AAPFU0939F1ZV');
    expect(group.valid).toBe(true);
  });

  it('requires an e-invoicing answer only from a regular shop', () => {
    const group = form();
    group.patchValue({ stateCode: '27', taxRegime: 'Regular', gstNumber: '27AAPFU0939F1ZV' });
    expect(group.hasError('eInvoiceStatusRequired')).toBe(true);

    group.controls.eInvoiceStatus.setValue('BelowThreshold');
    expect(group.valid).toBe(true);

    group.patchValue({ taxRegime: 'Composition', eInvoiceStatus: '' });
    expect(group.hasError('eInvoiceStatusRequired')).toBe(false);
  });

  it('forbids a GSTIN for a shop not registered under GST', () => {
    const group = form();
    group.patchValue({ stateCode: '27', taxRegime: 'Unregistered', gstNumber: '27AAPFU0939F1ZV' });
    expect(group.hasError('gstinNotAllowed')).toBe(true);

    group.controls.gstNumber.setValue('');
    expect(group.valid).toBe(true);
  });

  it('accepts 1 to 4 letters or digits as the series code', () => {
    const group = form();
    for (const [value, valid] of [
      ['B', true],
      ['ab12', true],
      ['', true],
      ['ABCDE', false],
      ['A/1', false],
    ] as const) {
      group.controls.invoiceSeriesCode.setValue(value);
      expect(group.controls.invoiceSeriesCode.valid, value).toBe(valid);
    }
  });

  it('maps the form to the request, sending e-invoicing only for a regular shop', () => {
    expect(
      toCreateShopTaxProfileRequest({
        legalName: '  Acme Traders LLP ',
        stateCode: '27',
        taxRegime: 'Regular',
        eInvoiceStatus: 'Exempt',
        invoiceSeriesCode: ' b2 ',
      }),
    ).toEqual({
      legalName: 'Acme Traders LLP',
      stateCode: '27',
      taxRegime: 'Regular',
      eInvoiceStatus: 'Exempt',
      invoiceSeriesCode: 'B2',
    });

    expect(
      toCreateShopTaxProfileRequest({
        legalName: ' ',
        stateCode: '07',
        taxRegime: 'Unregistered',
        eInvoiceStatus: 'Exempt',
        invoiceSeriesCode: '',
      }),
    ).toEqual({ legalName: null, stateCode: '07', taxRegime: 'Unregistered', eInvoiceStatus: null, invoiceSeriesCode: null });
  });
});
