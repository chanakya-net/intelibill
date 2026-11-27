import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { ShopDetailsDto, ShopTaxProfileDto, UpdateShopTaxProfileRequest } from '../../services/shop.service';
import { ShopTaxProfileFormComponent } from './shop-tax-profile-form.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<
  string,
  unknown
>;

describe('ShopTaxProfileFormComponent', () => {
  const details = (taxProfile?: Partial<ShopTaxProfileDto>, gstNumber: string | null = null): ShopDetailsDto => ({
    shopId: 'shop-1',
    name: 'Main',
    address: '42 MG Road',
    city: 'Pune',
    state: 'Maharashtra',
    pincode: '411001',
    contactPerson: null,
    mobileNumber: null,
    gstNumber,
    bankName: null,
    bankAccountNumber: null,
    bankAccountType: null,
    ifscCode: null,
    accountHolderName: null,
    taxProfile: {
      legalName: null,
      stateCode: null,
      taxRegime: 'NotDeclared',
      invoiceSeriesCode: 'A',
      eInvoiceStatus: 'NotAttested',
      eInvoiceAttestedAt: null,
      ...taxProfile,
    },
  });

  function setup(inputs: { details: ShopDetailsDto | null; canEdit?: boolean; disabled?: boolean }) {
    TestBed.configureTestingModule({
      imports: [
        ShopTaxProfileFormComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    });
    const fixture = TestBed.createComponent(ShopTaxProfileFormComponent);
    fixture.componentRef.setInput('details', inputs.details);
    fixture.componentRef.setInput('canEdit', inputs.canEdit ?? true);
    fixture.componentRef.setInput('disabled', inputs.disabled ?? false);
    fixture.detectChanges();
    const saved: UpdateShopTaxProfileRequest[] = [];
    fixture.componentInstance.saveRequested.subscribe((payload) => saved.push(payload));
    return { fixture, component: fixture.componentInstance, saved, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('warns that sales are blocked while the GST registration is not declared', () => {
    const { el } = setup({ details: details() });

    expect(el.querySelector('.tax-profile-warning')?.textContent).toContain('Sales are blocked');
  });

  it('does not warn once a registration is declared', () => {
    const { el, component } = setup({
      details: details({ taxRegime: 'Unregistered', stateCode: '27', invoiceSeriesCode: 'B' }),
    });

    expect(el.querySelector('.tax-profile-warning')).toBeNull();
    expect(component.form.controls.taxRegime.value).toBe('Unregistered');
    expect(component.form.controls.stateCode.value).toBe('27');
    expect(component.form.controls.invoiceSeriesCode.value).toBe('B');
  });

  it('patches a declared regular profile with its GSTIN and e-invoicing answer', () => {
    const { component } = setup({
      details: details(
        { taxRegime: 'Regular', stateCode: '27', legalName: 'Main Traders LLP', eInvoiceStatus: 'Exempt' },
        '27AAPFU0939F1ZV',
      ),
    });

    expect(component.form.getRawValue()).toEqual({
      legalName: 'Main Traders LLP',
      stateCode: '27',
      taxRegime: 'Regular',
      eInvoiceStatus: 'Exempt',
      invoiceSeriesCode: 'A',
      gstNumber: '27AAPFU0939F1ZV',
    });
  });

  it('asks whether e-invoicing applies only for a regular shop', () => {
    const { component, fixture, el } = setup({ details: details() });

    component.form.controls.taxRegime.setValue('Composition');
    fixture.detectChanges();
    expect(el.querySelector('#tax-profile-einvoice')).toBeNull();

    component.form.controls.taxRegime.setValue('Regular');
    fixture.detectChanges();
    expect(el.querySelector('#tax-profile-einvoice')).not.toBeNull();
  });

  it('does not save a GSTIN that fails its check digit or belongs to another state', () => {
    const { component, saved, fixture, el } = setup({ details: details() });
    component.form.patchValue({ stateCode: '27', taxRegime: 'Composition', gstNumber: '27AAPFU0939F1ZW' });

    component.onSave();
    fixture.detectChanges();
    expect(saved).toHaveLength(0);
    expect(el.textContent).toContain('check digit');

    component.form.controls.gstNumber.setValue('29AAGCB7383J1Z4');
    component.onSave();
    fixture.detectChanges();
    expect(saved).toHaveLength(0);
    expect(el.textContent).toContain('must match the selected state');
  });

  it('does not save a GSTIN for a shop not registered under GST', () => {
    const { component, saved } = setup({ details: details() });
    component.form.patchValue({ stateCode: '27', taxRegime: 'Unregistered', gstNumber: '27AAPFU0939F1ZV' });

    component.onSave();

    expect(saved).toHaveLength(0);
    expect(component.form.hasError('gstinNotAllowed')).toBe(true);
  });

  it('emits the tax profile request for a valid regular declaration', () => {
    const { component, saved } = setup({ details: details() });
    component.form.patchValue({
      legalName: ' Main Traders LLP ',
      stateCode: '27',
      taxRegime: 'Regular',
      gstNumber: '27aapfu0939f1zv',
      eInvoiceStatus: 'BelowThreshold',
      invoiceSeriesCode: 'b',
    });

    component.onSave();

    expect(saved).toEqual([
      {
        legalName: 'Main Traders LLP',
        stateCode: '27',
        taxRegime: 'Regular',
        gstNumber: '27AAPFU0939F1ZV',
        invoiceSeriesCode: 'B',
        eInvoiceStatus: 'BelowThreshold',
      },
    ]);
  });

  it('emits no GSTIN and no e-invoicing answer for an unregistered shop', () => {
    const { component, saved } = setup({ details: details() });
    component.form.patchValue({ stateCode: '07', taxRegime: 'Unregistered', eInvoiceStatus: 'Exempt' });

    component.onSave();

    expect(saved).toEqual([
      { legalName: null, stateCode: '07', taxRegime: 'Unregistered', gstNumber: null, invoiceSeriesCode: 'A', eInvoiceStatus: null },
    ]);
  });

  it('is read-only for anyone but the owner', () => {
    const { component, saved, el } = setup({ details: details(), canEdit: false });
    component.form.patchValue({ stateCode: '07', taxRegime: 'Unregistered' });

    component.onSave();

    expect(saved).toHaveLength(0);
    expect(component.form.disabled).toBe(true);
    expect(el.textContent).toContain('Only the shop owner');
    expect((el.querySelector('button.tax-profile-save') as HTMLButtonElement | null)?.disabled).toBe(true);
  });

  it('shows the server error and the saved confirmation it is given', () => {
    const { fixture, el } = setup({ details: details() });

    fixture.componentRef.setInput('errorMessage', 'errors.shops.gstinStateMismatch');
    fixture.detectChanges();
    expect(el.querySelector('.server-error')?.textContent).toContain('different state');

    fixture.componentRef.setInput('errorMessage', '');
    fixture.componentRef.setInput('saved', true);
    fixture.detectChanges();
    expect(el.querySelector('.server-error')).toBeNull();
    expect(el.querySelector('.tax-profile-saved')?.textContent).toContain('GST registration saved');
  });
});
