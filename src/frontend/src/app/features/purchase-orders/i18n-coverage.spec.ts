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

function resolvePath(data: Record<string, unknown>, path: string): unknown {
  return path.split('.').reduce<unknown>((current, segment) => {
    if (current === null || typeof current !== 'object' || Array.isArray(current)) {
      return undefined;
    }

    return (current as Record<string, unknown>)[segment];
  }, data);
}

describe('Purchase orders i18n coverage', () => {
  const localeFiles = readdirSync(I18N_DIR).filter((file) => file.endsWith('.json'));
  const enPurchaseOrderKeys = flattenKeys(loadJson('en-IN.json')['purchaseOrders']).sort();
  const requiredNamespaces = [
    'purchaseOrders.list',
    'purchaseOrders.builder',
    'purchaseOrders.detail',
    'purchaseOrders.receive',
    'purchaseOrders.receipts',
    'purchaseOrders.status',
    'purchaseOrders.actions',
    'purchaseOrders.errors',
    'purchaseOrders.print',
  ];

  it('en-IN has purchase order keys (sanity check)', () => {
    expect(enPurchaseOrderKeys.length).toBeGreaterThan(50);
  });

  for (const file of localeFiles) {
    it(`${file} contains all purchase order keys present in en-IN`, () => {
      const localePurchaseOrderKeys = new Set(flattenKeys(loadJson(file)['purchaseOrders']));
      const missing = enPurchaseOrderKeys.filter((key) => !localePurchaseOrderKeys.has(key));

      expect(missing, `${file} is missing: ${missing.join(', ')}`).toHaveLength(0);
    });

    it(`${file} contains required purchase order namespaces and shell key`, () => {
      const locale = loadJson(file);
      const missing = [...requiredNamespaces, 'shell.purchaseOrders'].filter(
        (key) => resolvePath(locale, key) === undefined,
      );

      expect(missing, `${file} is missing: ${missing.join(', ')}`).toHaveLength(0);
    });
  }
});
