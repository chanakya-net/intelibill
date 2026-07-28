import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
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

interface SourceFinding {
  readonly file: string;
  readonly text: string;
}

const NON_TRANSLATION_DOTTED_LITERALS = new Set([
  'inventory.auth.external.error',
  'inventory.auth.external.pending',
  'inventory.auth.last-email',
  'inventory.auth.last-identifier',
  'inventory.auth.session.local',
  'inventory.auth.session.temporary',
  'inventory.preferences.language',
]);

const INVARIANT_TRANSLATION_VALUES = new Set([
  'Intelibill',
  'Facebook',
  'Google',
  'HSN',
  'HSN/SAC',
  'SAC',
  'GST',
  'GSTIN',
  'IFSC',
  'SKU',
  'UOM',
  'MRP',
  'UPI',
  'A4',
  'PDF',
  'Excel',
  'Tally',
]);

const INTENTIONAL_TEMPLATE_TEXT = new Set(['GSTIN:', 'OZ']);
const INTENTIONAL_COMPONENT_CODE_TEXT = new Set(['Cash', 'UPI', 'Card', 'Credit']);

function localePath(locale: string): string {
  return join(I18N_DIR, `${locale}.json`);
}

function loadJson(locale: string): LocaleJson {
  return JSON.parse(readFileSync(localePath(locale), 'utf-8')) as LocaleJson;
}

function flattenLeaves(
  value: unknown,
  prefix = '',
  leaves = new Map<string, unknown>(),
): FlattenedLocale {
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

function placeholderSpacingMismatches(
  expectedLocale: FlattenedLocale,
  actualLocale: FlattenedLocale,
): string[] {
  return sortedKeys(expectedLocale).filter((key) => {
    const expected = expectedLocale.get(key);
    const actual = actualLocale.get(key);
    if (typeof expected !== 'string' || typeof actual !== 'string') {
      return false;
    }

    return Array.from(expected.matchAll(PLACEHOLDER_PATTERN)).some((match) => {
      const token = match[0];
      const expectedOffset = match.index;
      const actualOffset = actual.indexOf(token);
      if (actualOffset < 0) {
        return false;
      }

      const requiresLeadingSpace = expectedOffset > 0 && /\s/.test(expected[expectedOffset - 1]);
      const requiresTrailingSpace =
        expectedOffset + token.length < expected.length &&
        /\s/.test(expected[expectedOffset + token.length]);
      const hasLeadingSpace = actualOffset === 0 || /\s/.test(actual[actualOffset - 1]);
      const hasTrailingSpace =
        actualOffset + token.length === actual.length ||
        /\s/.test(actual[actualOffset + token.length]);
      const movedFromBoundaryIntoText =
        (expectedOffset === 0 || expectedOffset + token.length === expected.length) &&
        actualOffset > 0 &&
        actualOffset + token.length < actual.length;
      const touchesTranslatedWord =
        /[\p{L}\p{M}\p{N}]/u.test(actual[actualOffset - 1]) ||
        /[\p{L}\p{M}\p{N}]/u.test(actual[actualOffset + token.length]);

      return (
        (requiresLeadingSpace && !hasLeadingSpace) || (requiresTrailingSpace && !hasTrailingSpace)
        || (movedFromBoundaryIntoText && touchesTranslatedWord)
      );
    });
  });
}

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      return sourceFiles(path);
    }

    return /\.(?:html|ts)$/.test(entry) && !entry.endsWith('.spec.ts') ? [path] : [];
  });
}

function literalTranslationKeyReferences(): string[] {
  const appDirectory = join(process.cwd(), 'src/app');
  const dottedLiteralPattern = /["'`]([a-z][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_-]+)+)["'`]/g;
  const namespaces = new Set(Object.keys(loadJson(DEFAULT_LOCALE)));

  return Array.from(
    new Set(
      sourceFiles(appDirectory).flatMap((file) =>
        Array.from(
          readFileSync(file, 'utf-8').matchAll(dottedLiteralPattern),
          (match) => match[1],
        ).filter(
          (key) => namespaces.has(key.split('.')[0]) && !NON_TRANSLATION_DOTTED_LITERALS.has(key),
        ),
      ),
    ),
  ).sort();
}

function templateContents(file: string): string[] {
  const source = readFileSync(file, 'utf-8');

  if (file.endsWith('.html')) {
    return [source];
  }

  return Array.from(source.matchAll(/template\s*:\s*`([\s\S]*?)`/g), (match) => match[1]);
}

function hardCodedTemplateText(): SourceFinding[] {
  const appDirectory = join(process.cwd(), 'src/app');
  const textNodePattern = />([^<]+)</gs;

  return sourceFiles(appDirectory).flatMap((file) =>
    templateContents(file).flatMap((template) =>
      Array.from(template.matchAll(textNodePattern)).flatMap((match) => {
        const rawText = match[1];
        if (/@(?:if|for|else|empty|switch|case|default)\b/.test(rawText) || /["=]/.test(rawText)) {
          return [];
        }

        const text = rawText
          .replace(/{{[\s\S]*?}}/g, '')
          .replace(/\s+/g, ' ')
          .trim();
        return /[A-Za-z]{2}/.test(text) && !INTENTIONAL_TEMPLATE_TEXT.has(text)
          ? [{ file, text }]
          : [];
      }),
    ),
  );
}

function hardCodedUserFacingAttributes(): SourceFinding[] {
  const appDirectory = join(process.cwd(), 'src/app');
  const attributePattern =
    /(?<![.\w-])(?:aria-label|placeholder|alt|title|label|header)\s*=\s*["']([A-Za-z][^"']*)["']/g;

  return sourceFiles(appDirectory).flatMap((file) =>
    templateContents(file).flatMap((template) =>
      Array.from(template.matchAll(attributePattern), (match) => ({
        file,
        text: match[1],
      })),
    ),
  );
}

function hardCodedComponentCodeText(): SourceFinding[] {
  const appDirectory = join(process.cwd(), 'src/app');
  const patterns = [
    /\b(?:summary|detail|label|placeholder|title|header|ariaLabel)\s*:\s*["']([A-Z][^"']*[A-Za-z][^"']*)["']/g,
    /\|\|\s*["']((?:Failed|Unable|Could not|Unknown|Please|No)\b[^"']*)["']/g,
    /\.(?:set|showError|showSuccess|showWarn|showInfo)\(\s*(?:\[\s*)?["']([A-Z][^"']*(?:\s|[.!?])[^"']*)["']/g,
  ];

  return sourceFiles(appDirectory)
    .filter((file) => file.endsWith('.ts'))
    .flatMap((file) => {
      const source = readFileSync(file, 'utf-8');
      return patterns.flatMap((pattern) =>
        Array.from(source.matchAll(pattern), (match) => match[1])
          .filter((text) => !INTENTIONAL_COMPONENT_CODE_TEXT.has(text))
          .map((text) => ({ file, text })),
      );
    });
}

function untranslatedEnglishCopies(
  expectedLocale: FlattenedLocale,
  actualLocale: FlattenedLocale,
): string[] {
  return sortedKeys(expectedLocale).filter((key) => {
    const expected = expectedLocale.get(key);
    const actual = actualLocale.get(key);

    return (
      typeof expected === 'string' &&
      expected.trim().length > 1 &&
      expected === actual &&
      !isInvariantTranslationValue(expected)
    );
  });
}

function isInvariantTranslationValue(value: string): boolean {
  if (INVARIANT_TRANSLATION_VALUES.has(value)) {
    return true;
  }

  const remainingText = value
    .replace(PLACEHOLDER_PATTERN, '')
    .replace(/\b(?:Intelibill|Facebook|Google|HSN|SAC|GSTIN|GST|IFSC|SKU|UOM|MRP|UPI|A4|PDF|Excel|Tally)\b/gi, '')
    .replace(/[^\p{L}]+/gu, '');

  return remainingText.length === 0;
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

  it('defines every literal translation key referenced by application code', () => {
    const missing = literalTranslationKeyReferences().filter((key) => !enKeySet.has(key));

    expect(
      missing,
      `Missing translation keys referenced by the app: ${missing.join(', ')}`,
    ).toHaveLength(0);
  });

  it('does not contain hard-coded prose in templates', () => {
    const findings = hardCodedTemplateText();

    expect(
      findings,
      `Hard-coded template text:\n${findings.map(({ file, text }) => `${file}: ${text}`).join('\n')}`,
    ).toHaveLength(0);
  });

  it('does not contain hard-coded user-facing attributes', () => {
    const findings = hardCodedUserFacingAttributes();

    expect(
      findings,
      `Hard-coded user-facing attributes:\n${findings
        .map(({ file, text }) => `${file}: ${text}`)
        .join('\n')}`,
    ).toHaveLength(0);
  });

  it('does not hard-code user-facing messages or option labels in component code', () => {
    const findings = hardCodedComponentCodeText();

    expect(
      findings,
      `Hard-coded component UI text:\n${findings
        .map(({ file, text }) => `${file}: ${text}`)
        .join('\n')}`,
    ).toHaveLength(0);
  });

  for (const locale of SUPPORTED_LANGUAGES.filter(
    (supportedLocale) => supportedLocale !== DEFAULT_LOCALE,
  )) {
    it(`${locale} has exactly the ${DEFAULT_LOCALE} translation keys`, () => {
      const localeKeys = sortedKeys(flattenLeaves(loadJson(locale)));
      const localeKeySet = new Set(localeKeys);
      const missing = missingKeys(enKeys, localeKeySet);
      const extra = extraKeys(enKeySet, localeKeys);

      expect([...missing, ...extra], formatKeyDiff(locale, missing, extra)).toHaveLength(0);
    });

    it(`${locale} does not copy English UI text`, () => {
      const copied = untranslatedEnglishCopies(enLocale, flattenLeaves(loadJson(locale)));

      expect(
        copied,
        `${locale} contains untranslated English values: ${copied.join(', ')}`,
      ).toHaveLength(0);
    });
  }

  for (const locale of SUPPORTED_LANGUAGES) {
    it(`${locale} preserves ${DEFAULT_LOCALE} interpolation placeholders`, () => {
      const mismatches = placeholderMismatches(enLocale, flattenLeaves(loadJson(locale)));

      expect(mismatches, formatPlaceholderDiff(locale, mismatches)).toHaveLength(0);
    });

    it(`${locale} preserves required spacing around interpolation placeholders`, () => {
      const mismatches = placeholderSpacingMismatches(enLocale, flattenLeaves(loadJson(locale)));

      expect(
        mismatches,
        `${locale} has interpolation spacing mismatches: ${mismatches.join(', ')}`,
      ).toHaveLength(0);
    });
  }

  for (const locale of SUPPORTED_LANGUAGES) {
    it(`${locale} includes the customer directory keys`, () => {
      const localeJson = loadJson(locale);
      const missing = requiredCustomerKeys.filter(
        (key) => resolvePath(localeJson, key) === undefined,
      );

      expect(missing, `Missing customer keys in ${locale}: ${missing.join(', ')}`).toHaveLength(0);
    });
  }
});
