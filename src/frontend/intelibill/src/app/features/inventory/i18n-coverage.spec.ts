import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const I18N_DIR = join(process.cwd(), 'public/assets/i18n');

function loadJson(file: string): Record<string, unknown> {
  return JSON.parse(readFileSync(join(I18N_DIR, file), 'utf-8')) as Record<string, unknown>;
}

function flattenKeys(value: unknown, prefix = ''): string[] {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return prefix ? [prefix] : [];
  }

  return Object.entries(value as Record<string, unknown>).flatMap(([key, child]) =>
    flattenKeys(child, prefix ? `${prefix}.${key}` : key),
  );
}

describe('Inventory adjustment i18n coverage', () => {
  const localeFiles = readdirSync(I18N_DIR).filter((file) => file.endsWith('.json'));
  const enKeys = flattenKeys(loadJson('en-IN.json')).sort();

  for (const file of localeFiles) {
    it(`${file} contains every supported translation key`, () => {
      const localeKeys = new Set(flattenKeys(loadJson(file)));
      const missing = enKeys.filter((key) => !localeKeys.has(key));

      expect(missing, `${file} is missing: ${missing.join(', ')}`).toHaveLength(0);
    });
  }
});
