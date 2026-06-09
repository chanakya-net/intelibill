import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import { SUPPORTED_LANGUAGES } from '../../core/i18n/language.constants';

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

describe('Dashboard i18n coverage', () => {
  const localeFiles = readdirSync(I18N_DIR).filter((file) => file.endsWith('.json'));
  const enDashboardKeys = flattenKeys(loadJson('en-IN.json')['dashboard']).sort();

  it('en-IN has dashboard keys (sanity check)', () => {
    expect(enDashboardKeys.length).toBeGreaterThan(20);
  });

  it('has a dashboard section for every supported language', () => {
    const missingFiles = SUPPORTED_LANGUAGES.filter((locale) => !localeFiles.includes(`${locale}.json`));

    expect(
      missingFiles,
      `Missing supported locale files: ${missingFiles.map((locale) => `${locale}.json`).join(', ')}`,
    ).toHaveLength(0);
  });

  for (const file of localeFiles) {
    it(`${file} contains all dashboard keys present in en-IN`, () => {
      const localeDashboardKeys = new Set(flattenKeys(loadJson(file)['dashboard']));
      const missing = enDashboardKeys.filter((key) => !localeDashboardKeys.has(key));

      expect(missing, `${file} is missing: ${missing.join(', ')}`).toHaveLength(0);
    });
  }
});
