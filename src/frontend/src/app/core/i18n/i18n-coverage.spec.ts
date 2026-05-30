import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import { SUPPORTED_LANGUAGES } from './language.constants';

const I18N_DIR = join(process.cwd(), 'public/assets/i18n');
const DEFAULT_LOCALE = 'en-IN';
const PLACEHOLDER_PATTERN =
  /{{\s*([A-Za-z_][A-Za-z0-9_.]*)\s*}}|(?<!{){\s*([A-Za-z_][A-Za-z0-9_.]*)\s*}(?!})/g;

type LocaleJson = Record<string, unknown>;
type FlattenedLocale = Map<string, unknown>;

interface PlaceholderMismatch {
  key: string;
  expected: string[];
  actual: string[];
}

function localePath(locale: string): string {
  return join(I18N_DIR, `${locale}.json`);
}

function loadJson(locale: string): LocaleJson {
  return JSON.parse(readFileSync(localePath(locale), 'utf-8')) as LocaleJson;
}

function flattenLeaves(value: unknown, prefix = '', leaves = new Map<string, unknown>()): FlattenedLocale {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    if (prefix) {
      leaves.set(prefix, value);
    }

    return leaves;
  }

  for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
    flattenLeaves(child, prefix ? `${prefix}.${key}` : key, leaves);
  }

  return leaves;
}

function resolvePath(locale: LocaleJson, path: string): unknown {
  return path.split('.').reduce<unknown>((current, segment) => {
    if (current === null || typeof current !== 'object' || Array.isArray(current)) {
      return undefined;
    }

    return (current as Record<string, unknown>)[segment];
  }, locale);
}

function sortedKeys(locale: FlattenedLocale): string[] {
  return Array.from(locale.keys()).sort();
}

function missingKeys(expected: string[], actual: Set<string>): string[] {
  return expected.filter((key) => !actual.has(key));
}

function extraKeys(expected: Set<string>, actual: string[]): string[] {
  return actual.filter((key) => !expected.has(key));
}

function placeholders(value: string): string[] {
  const matches = Array.from(value.matchAll(PLACEHOLDER_PATTERN), (match) => match[1] ?? match[2]);
  return Array.from(new Set(matches)).sort();
}

function placeholderMismatches(
  expectedLocale: FlattenedLocale,
  actualLocale: FlattenedLocale,
): PlaceholderMismatch[] {
  return sortedKeys(expectedLocale).flatMap((key) => {
    const expectedValue = expectedLocale.get(key);
    const actualValue = actualLocale.get(key);

    if (typeof expectedValue !== 'string' && typeof actualValue !== 'string') {
      return [];
    }

    const expected = typeof expectedValue === 'string' ? placeholders(expectedValue) : [];
    const actual = typeof actualValue === 'string' ? placeholders(actualValue) : [];

    return expected.join('|') === actual.join('|') ? [] : [{ key, expected, actual }];
  });
}

function formatKeyDiff(locale: string, missing: string[], extra: string[]): string {
  return [
    `${locale} schema differs from ${DEFAULT_LOCALE}`,
    `missing: ${missing.length ? missing.join(', ') : '(none)'}`,
    `extra: ${extra.length ? extra.join(', ') : '(none)'}`,
  ].join('\n');
}

function formatPlaceholderDiff(locale: string, mismatches: PlaceholderMismatch[]): string {
  return [
    `${locale} placeholder mismatches:`,
    ...mismatches.map(
      ({ key, expected, actual }) =>
        `${key}: expected {${expected.join(', ') || 'none'}}, actual {${actual.join(', ') || 'none'}}`,
    ),
  ].join('\n');
}

describe('Global i18n coverage', () => {
  const enLocale = flattenLeaves(loadJson(DEFAULT_LOCALE));
  const enKeys = sortedKeys(enLocale);
  const enKeySet = new Set(enKeys);
  const requiredCustomerKeys = [
    'customers.summary.totalCustomers',
    'customers.summary.outstandingBalance',
    'customers.summary.overdueCount',
    'customers.summary.totalCreditIssued',
    'customers.summary.accountsWithCredit',
    'customers.summary.monthlyRevenue',
    'customers.summary.filteredRows',
    'customers.creditLimit',
    'customers.status',
    'customers.usage',
    'customers.overdue',
    'customers.inCredit',
    'customers.newTransaction',
    'customers.showingCount',
    'customers.searchPlaceholder',
  ] as const;

  it('has a locale file for every supported language', () => {
    const missingFiles = SUPPORTED_LANGUAGES.filter((locale) => !existsSync(localePath(locale)));

    expect(
      missingFiles,
      `Missing supported locale files: ${missingFiles.map((locale) => `${locale}.json`).join(', ')}`,
    ).toHaveLength(0);
  });

  for (const locale of SUPPORTED_LANGUAGES.filter((supportedLocale) => supportedLocale !== DEFAULT_LOCALE)) {
    it(`${locale} has exactly the ${DEFAULT_LOCALE} translation keys`, () => {
      const localeKeys = sortedKeys(flattenLeaves(loadJson(locale)));
      const localeKeySet = new Set(localeKeys);
      const missing = missingKeys(enKeys, localeKeySet);
      const extra = extraKeys(enKeySet, localeKeys);

      expect([...missing, ...extra], formatKeyDiff(locale, missing, extra)).toHaveLength(0);
    });
  }

  for (const locale of SUPPORTED_LANGUAGES) {
    it(`${locale} preserves ${DEFAULT_LOCALE} interpolation placeholders`, () => {
      const mismatches = placeholderMismatches(enLocale, flattenLeaves(loadJson(locale)));

      expect(mismatches, formatPlaceholderDiff(locale, mismatches)).toHaveLength(0);
    });
  }

  for (const locale of SUPPORTED_LANGUAGES) {
    it(`${locale} includes the customer directory keys`, () => {
      const localeJson = loadJson(locale);
      const missing = requiredCustomerKeys.filter((key) => resolvePath(localeJson, key) === undefined);

      expect(missing, `Missing customer keys in ${locale}: ${missing.join(', ')}`).toHaveLength(0);
    });
  }
});
