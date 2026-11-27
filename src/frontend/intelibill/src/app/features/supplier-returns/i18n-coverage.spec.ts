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
describe('Supplier return translations', () => {
  it.each([bn, gu, hi, kn, ml, mr, ta, te])(
    'has complete nonempty labels and commercial disclaimer',
    (locale) => {
      expect(Object.keys(locale.supplierReturns).sort()).toEqual(
        Object.keys(en.supplierReturns).sort(),
      );
      expect(Object.values(locale.supplierReturns).every((v) => v.length > 0)).toBe(true);
    },
  );
});
