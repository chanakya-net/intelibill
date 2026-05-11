import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const I18N_DIR = join(process.cwd(), 'public/assets/i18n');

function loadJson(file: string): Record<string, unknown> {
  return JSON.parse(readFileSync(join(I18N_DIR, file), 'utf-8')) as Record<string, unknown>;
}

function getDiscounts(data: Record<string, unknown>): Record<string, unknown> {
  return (data['discounts'] ?? {}) as Record<string, unknown>;
}

function flattenKeys(value: unknown, prefix = ''): string[] {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return prefix ? [prefix] : [];
  }

  return Object.entries(value as Record<string, unknown>).flatMap(([key, child]) =>
    flattenKeys(child, prefix ? `${prefix}.${key}` : key),
  );
}

describe('Discounts i18n coverage', () => {
  const localeFiles = readdirSync(I18N_DIR).filter((f) => f.endsWith('.json'));
  const enDiscountsKeys = flattenKeys(getDiscounts(loadJson('en-IN.json'))).sort();

  it('en-IN has discounts keys (sanity check)', () => {
    expect(enDiscountsKeys.length).toBeGreaterThan(10);
  });

  for (const file of localeFiles) {
    it(`${file} contains all discounts keys present in en-IN`, () => {
      const localeDiscountsKeys = new Set(flattenKeys(getDiscounts(loadJson(file))));
      const missing = enDiscountsKeys.filter((key) => !localeDiscountsKeys.has(key));
      expect(missing, `${file} is missing: ${missing.join(', ')}`).toHaveLength(0);
    });
  }
});

