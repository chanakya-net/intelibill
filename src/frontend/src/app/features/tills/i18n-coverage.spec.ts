import { describe, it, expect } from 'vitest';
import en from '../../../../public/assets/i18n/en-IN.json';
import bn from '../../../../public/assets/i18n/bn-IN.json';
import gu from '../../../../public/assets/i18n/gu-IN.json';
import hi from '../../../../public/assets/i18n/hi-IN.json';
import kn from '../../../../public/assets/i18n/kn-IN.json';
import ml from '../../../../public/assets/i18n/ml-IN.json';
import mr from '../../../../public/assets/i18n/mr-IN.json';
import ta from '../../../../public/assets/i18n/ta-IN.json';
import te from '../../../../public/assets/i18n/te-IN.json';
function leaves(value: unknown, prefix = ''): string[] {
  return typeof value === 'string'
    ? [prefix]
    : Object.entries(value as object).flatMap(([key, nested]) =>
        leaves(nested, `${prefix}.${key}`),
      );
}
describe('Till translation coverage', () => {
  it.each([bn, gu, hi, kn, ml, mr, ta, te])(
    'keeps complete nonempty labels and recorded/unknown/count-exclusion meanings',
    (locale) => {
      expect(leaves(locale.tills).sort()).toEqual(leaves(en.tills).sort());
      expect(locale.tills.singleTender.length).toBeGreaterThan(0);
      expect(locale.tills.excluded.length).toBeGreaterThan(0);
      expect(locale.tills.unknownAmount.length).toBeGreaterThan(0);
    },
  );
});
