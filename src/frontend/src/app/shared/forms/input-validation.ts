import { AbstractControl, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';

const PHONE_NUMBER_PATTERN = /^\+?[0-9]{7,15}$/;
const HSN_SAC_PATTERN = /^\s*\d{4,8}\s*$/;
const INDIA_GST_PATTERN = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/i;
const INDIA_IFSC_PATTERN = /^[A-Z]{4}0[A-Z0-9]{6}$/i;

export interface CommonInputOptions {
  readonly required?: boolean;
  readonly maxLength?: number;
}

function trimmedRequired(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (typeof value === 'string') {
    return value.trim().length > 0 ? null : { required: true };
  }

  return Validators.required(control);
}

function positive(control: AbstractControl): ValidationErrors | null {
  const value = Number(control.value);
  return Number.isFinite(value) && value > 0 ? null : { min: { min: 0, actual: control.value } };
}

function integer(control: AbstractControl): ValidationErrors | null {
  const value = Number(control.value);
  return Number.isFinite(value) && Number.isInteger(value) ? null : { integer: true };
}

function normalizedPattern(pattern: RegExp): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const rawValue = control.value;
    if (rawValue === null || rawValue === undefined) return null;

    const value = typeof rawValue === 'string' ? rawValue.trim() : String(rawValue);
    if (value.length === 0) return null;

    pattern.lastIndex = 0;
    return pattern.test(value)
      ? null
      : { pattern: { requiredPattern: pattern.toString(), actualValue: rawValue } };
  };
}

function fractionDigits(value: number): number {
  if (!Number.isFinite(value) || Number.isInteger(value)) return 0;

  const [coefficient, exponentText] = value.toString().toLowerCase().split('e');
  const coefficientDigits = coefficient.split('.')[1]?.length ?? 0;
  const exponent = exponentText === undefined ? 0 : Number(exponentText);
  return Math.max(0, coefficientDigits - exponent);
}

export const InputValidators = {
  requiredText(maxLength?: number): ValidatorFn[] {
    return [trimmedRequired, ...(maxLength === undefined ? [] : [Validators.maxLength(maxLength)])];
  },

  optionalText(maxLength: number): ValidatorFn[] {
    return [Validators.maxLength(maxLength)];
  },

  email(options: CommonInputOptions = {}): ValidatorFn[] {
    const { required = true, maxLength = 256 } = options;
    return [
      ...(required ? [trimmedRequired] : []),
      Validators.email,
      Validators.maxLength(maxLength),
    ];
  },

  phoneNumber(options: CommonInputOptions = {}): ValidatorFn[] {
    const { required = true, maxLength = 32 } = options;
    return [
      ...(required ? [trimmedRequired] : []),
      normalizedPattern(PHONE_NUMBER_PATTERN),
      Validators.maxLength(maxLength),
    ];
  },

  password(options: CommonInputOptions = {}): ValidatorFn[] {
    const { required = true, maxLength = 100 } = options;
    return [
      ...(required ? [Validators.required] : []),
      Validators.minLength(8),
      Validators.maxLength(maxLength),
    ];
  },

  positiveNumber(): ValidatorFn[] {
    return [Validators.required, positive];
  },

  positiveInteger(): ValidatorFn[] {
    return [Validators.required, Validators.min(1), integer];
  },

  nonNegativeNumber(): ValidatorFn[] {
    return [Validators.required, Validators.min(0)];
  },

  percentage(): ValidatorFn[] {
    return [Validators.required, Validators.min(0), Validators.max(100)];
  },

  hsnSacCode(maxLength = 20): ValidatorFn[] {
    return [Validators.maxLength(maxLength), normalizedPattern(HSN_SAC_PATTERN)];
  },

  gstNumber(maxLength = 20): ValidatorFn[] {
    return [Validators.maxLength(maxLength), normalizedPattern(INDIA_GST_PATTERN)];
  },

  ifscCode(maxLength = 20): ValidatorFn[] {
    return [Validators.maxLength(maxLength), normalizedPattern(INDIA_IFSC_PATTERN)];
  },

  maxFractionDigits(digits: number): ValidatorFn {
    return (control: AbstractControl): ValidationErrors | null => {
      const value = control.value;
      if (value === null || value === undefined || value === '') return null;

      return fractionDigits(Number(value)) > digits
        ? { maxFractionDigits: { requiredDigits: digits } }
        : null;
    };
  },

  fieldsMatch(firstField: string, secondField: string, errorKey: string): ValidatorFn {
    return (control: AbstractControl): ValidationErrors | null => {
      const firstValue = control.get(firstField)?.value;
      const secondValue = control.get(secondField)?.value;
      if (!firstValue || !secondValue || firstValue === secondValue) return null;

      return { [errorKey]: true };
    };
  },

  fieldsDiffer(firstField: string, secondField: string, errorKey: string): ValidatorFn {
    return (control: AbstractControl): ValidationErrors | null => {
      const firstValue = control.get(firstField)?.value;
      const secondValue = control.get(secondField)?.value;
      if (!firstValue || !secondValue || firstValue !== secondValue) return null;

      return { [errorKey]: true };
    };
  },

  fieldLessThanOrEqual(valueField: string, limitField: string, errorKey: string): ValidatorFn {
    return (control: AbstractControl): ValidationErrors | null => {
      const value = Number(control.get(valueField)?.value);
      const limit = Number(control.get(limitField)?.value);
      if (!Number.isFinite(value) || !Number.isFinite(limit) || value <= limit) return null;

      return { [errorKey]: true };
    };
  },
} as const;
