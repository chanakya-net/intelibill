/** Rupee text such as "1,499.00" ↔ whole paise, by string arithmetic only (no floating point). */
const maxPaise = 1_000_000_000_000;
const rupees = /^(\d{1,11})(?:\.(\d{1,2}))?$/;

export const invalid = Symbol('invalid');
export type Parsed<T> = T | null | typeof invalid;

/** Blank → null (missing); "0" → 0 (an explicit fact); anything else malformed → invalid. */
export function rupeesToPaise(text: string): Parsed<number> {
  const value = text.trim().replaceAll(',', '');
  if (value === '') return null;
  const match = rupees.exec(value);
  if (!match) return invalid;
  const paise = Number(match[1]) * 100 + Number((match[2] ?? '').padEnd(2, '0'));
  return paise <= maxPaise ? paise : invalid;
}

export function paiseToRupees(paise: number | null): string {
  if (paise === null) return '';
  const whole = Math.trunc(paise / 100).toString();
  const fraction = (paise % 100).toString().padStart(2, '0');
  return whole + '.' + fraction;
}

/** Display form, e.g. ₹1,499.00. */
export function formatInr(paise: number | null): string {
  if (paise === null) return 'Not set';
  const [whole, fraction] = paiseToRupees(paise).split('.');
  return '₹' + whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + '.' + fraction;
}

/** Blank → null; whole numbers 1–100,000 only. There is no unlimited value. */
export function parseCount(text: string): Parsed<number> {
  const value = text.trim();
  if (value === '') return null;
  if (!/^\d{1,6}$/.test(value)) return invalid;
  const count = Number(value);
  return count >= 1 && count <= 100_000 ? count : invalid;
}

export function parseText(text: string): string | null {
  const value = text.trim();
  return value === '' ? null : value;
}
