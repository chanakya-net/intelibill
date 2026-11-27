import { Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { TranslocoService } from '@ngneat/transloco';
import { map } from 'rxjs';

import { InputValidators } from '../../../../shared/forms/input-validation';
import {
  AttestedEInvoiceStatus,
  CreateShopTaxProfileRequest,
  DeclarableTaxRegime,
  ShopTaxProfileDto,
} from '../../services/shop.service';

// The GST registration fields shared by the create-shop overlay and the manage-shop tax profile section. The GSTIN
// control itself is `gstNumber`: on a new shop it travels as the request's top-level GSTIN.

export type TaxRegimeSelection = DeclarableTaxRegime | '';
export type EInvoiceStatusSelection = AttestedEInvoiceStatus | '';

export const DEFAULT_INVOICE_SERIES_CODE = 'A';
export const LEGAL_NAME_MAX_LENGTH = 200;
const INVOICE_SERIES_CODE_PATTERN = /^[A-Z0-9]{1,4}$/i;

export const TAX_REGIME_OPTIONS: readonly { readonly value: DeclarableTaxRegime; readonly labelKey: string }[] = [
  { value: 'Regular', labelKey: 'shops.taxProfile.regimes.regular' },
  { value: 'Composition', labelKey: 'shops.taxProfile.regimes.composition' },
  { value: 'Unregistered', labelKey: 'shops.taxProfile.regimes.unregistered' },
];

export const E_INVOICE_STATUS_OPTIONS: readonly { readonly value: AttestedEInvoiceStatus; readonly labelKey: string }[] = [
  { value: 'BelowThreshold', labelKey: 'shops.taxProfile.eInvoiceStatuses.belowThreshold' },
  { value: 'Exempt', labelKey: 'shops.taxProfile.eInvoiceStatuses.exempt' },
  { value: 'Required', labelKey: 'shops.taxProfile.eInvoiceStatuses.required' },
];

export type LabelledOption<T> = T & { readonly label: string };

/**
 * Options carrying their label in the active language. PrimeNG names the combobox and each option from
 * `optionLabel`, so a translation key there would be read aloud. Must be called in an injection context.
 */
export function translatedOptions<T extends { readonly labelKey: string }>(
  transloco: TranslocoService,
  options: readonly T[],
): Signal<LabelledOption<T>[]> {
  const label = (): LabelledOption<T>[] => options.map((option) => ({ ...option, label: transloco.translate(option.labelKey) }));
  return toSignal(transloco.selectTranslation().pipe(map(label)), { initialValue: label() });
}

export interface TaxProfileFormValue {
  readonly legalName: string;
  readonly stateCode: string;
  readonly taxRegime: TaxRegimeSelection;
  readonly eInvoiceStatus: EInvoiceStatusSelection;
  readonly invoiceSeriesCode: string;
}

/** Control definitions for `FormBuilder.nonNullable.group`; pair them with {@link taxRegistrationValidator}. */
export function createTaxProfileControls() {
  return {
    legalName: ['', InputValidators.optionalText(LEGAL_NAME_MAX_LENGTH)],
    stateCode: ['', Validators.required],
    taxRegime: ['' as TaxRegimeSelection, Validators.required],
    eInvoiceStatus: ['' as EInvoiceStatusSelection],
    invoiceSeriesCode: [
      DEFAULT_INVOICE_SERIES_CODE,
      [Validators.maxLength(4), Validators.pattern(INVOICE_SERIES_CODE_PATTERN)],
    ],
  };
}

/** Form values for a declared profile; an undeclared one starts empty with the default series. */
export function taxProfileFormValue(profile: ShopTaxProfileDto | undefined): TaxProfileFormValue {
  const regime = profile?.taxRegime;
  const eInvoice = profile?.eInvoiceStatus;
  return {
    legalName: profile?.legalName ?? '',
    stateCode: profile?.stateCode ?? '',
    taxRegime: regime && regime !== 'NotDeclared' ? regime : '',
    eInvoiceStatus: eInvoice && eInvoice !== 'NotAttested' ? eInvoice : '',
    invoiceSeriesCode: profile?.invoiceSeriesCode || DEFAULT_INVOICE_SERIES_CODE,
  };
}

/**
 * The rules that tie the regime to the GSTIN and e-invoicing answer: a regular or composition shop needs a GSTIN from
 * the chosen state, a shop not registered under GST must not have one, and a regular shop must say whether
 * e-invoicing applies. The GSTIN's own shape and check digit are checked on its control.
 */
export function taxRegistrationValidator(): ValidatorFn {
  const stateMatch = InputValidators.gstinMatchesState('gstNumber', 'stateCode');
  return (group: AbstractControl): ValidationErrors | null => {
    const regime = group.get('taxRegime')?.value as TaxRegimeSelection;
    const gstin = String(group.get('gstNumber')?.value ?? '').trim();
    const eInvoice = group.get('eInvoiceStatus')?.value as EInvoiceStatusSelection;

    if (regime === 'Unregistered') {
      return gstin ? { gstinNotAllowed: true } : null;
    }
    if (regime !== 'Regular' && regime !== 'Composition') return null;

    const errors: ValidationErrors = {
      ...(gstin ? stateMatch(group) : { gstinRequired: true }),
      ...(regime === 'Regular' && !eInvoice ? { eInvoiceStatusRequired: true } : {}),
    };
    return Object.keys(errors).length ? errors : null;
  };
}

export function toCreateShopTaxProfileRequest(value: TaxProfileFormValue): CreateShopTaxProfileRequest {
  const legalName = value.legalName.trim();
  const seriesCode = value.invoiceSeriesCode.trim().toUpperCase();
  const regime = value.taxRegime as DeclarableTaxRegime;
  return {
    legalName: legalName.length > 0 ? legalName : null,
    stateCode: value.stateCode,
    taxRegime: regime,
    invoiceSeriesCode: seriesCode.length > 0 ? seriesCode : null,
    eInvoiceStatus: regime === 'Regular' && value.eInvoiceStatus ? value.eInvoiceStatus : null,
  };
}

export function toOptionalGstin(value: string): string | null {
  const gstin = value.trim().toUpperCase();
  return gstin.length > 0 ? gstin : null;
}
