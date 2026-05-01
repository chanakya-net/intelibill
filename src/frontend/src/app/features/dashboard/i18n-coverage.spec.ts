/**
 * Localization coverage spec for the dashboard feature.
 *
 * Verifies that:
 * 1. All locale files contain every dashboard key present in the reference (en-IN) file.
 * 2. No dashboard key in hi-IN is missing a translation (still using the English text as a fallback
 *    for keys that are intentionally the same in Hindi — e.g. UPI — we allow those explicitly).
 */
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { describe, it, expect } from 'vitest';

const I18N_DIR = join(process.cwd(), 'public/assets/i18n');

const INTENTIONAL_SAME_IN_HI = new Set([
  'paymentMixUpi', // UPI is an acronym used as-is in Hindi
]);

function loadJson(file: string): Record<string, unknown> {
  return JSON.parse(readFileSync(join(I18N_DIR, file), 'utf-8')) as Record<string, unknown>;
}

function getDashboard(data: Record<string, unknown>): Record<string, string> {
  return (data['dashboard'] ?? {}) as Record<string, string>;
}

describe('Dashboard i18n coverage', () => {
  const localeFiles = readdirSync(I18N_DIR).filter((f: string) => f.endsWith('.json'));
  const enData = loadJson('en-IN.json');
  const enDashboard = getDashboard(enData);
  const enKeys = Object.keys(enDashboard);

  it('en-IN has at least 50 dashboard keys (sanity check)', () => {
    expect(enKeys.length).toBeGreaterThanOrEqual(50);
  });

  for (const file of localeFiles) {
    it(`${file} contains all dashboard keys present in en-IN`, () => {
      const data = loadJson(file);
      const dashboard = getDashboard(data);
      const missing = enKeys.filter((k) => !(k in dashboard));
      expect(missing, `${file} is missing: ${missing.join(', ')}`).toHaveLength(0);
    });
  }

  it('hi-IN has Hindi translations for all translatable dashboard keys', () => {
    const hiData = loadJson('hi-IN.json');
    const hiDashboard = getDashboard(hiData);
    const stillEnglish = enKeys.filter(
      (k) => !INTENTIONAL_SAME_IN_HI.has(k) && hiDashboard[k] === enDashboard[k],
    );
    expect(
      stillEnglish,
      `hi-IN keys still using English text: ${stillEnglish.join(', ')}`,
    ).toHaveLength(0);
  });
});
