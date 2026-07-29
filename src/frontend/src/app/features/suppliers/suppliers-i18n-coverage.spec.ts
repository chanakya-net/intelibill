import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { SUPPORTED_LANGUAGES } from '../../core/i18n/language.constants';

const REQUIRED_SUPPLIER_KEYS = ['balanceDue', 'all', 'goodsReceived', 'payments'] as const;

describe('Supplier i18n coverage', () => {
  it('defines the ledger control keys in every supported locale', () => {
    for (const locale of SUPPORTED_LANGUAGES) {
      const localePath = join(process.cwd(), 'public/assets/i18n', `${locale}.json`);
      const translations = JSON.parse(readFileSync(localePath, 'utf-8')) as {
        suppliers: Record<string, unknown>;
      };

      for (const key of REQUIRED_SUPPLIER_KEYS) {
        expect(translations.suppliers[key], `${locale} is missing suppliers.${key}`).toEqual(
          expect.any(String),
        );
        expect(translations.suppliers[key], `${locale} has an empty suppliers.${key}`).not.toBe('');
      }
    }
  });
});
