import { describe, expect, it } from 'vitest';

import contract from '../../../../../../tests/contracts/quantity-units-v1.json';
import {
  QUANTITY_CUSTOM_DOCUMENT_UNIT,
  QUANTITY_SCALE_MAX,
  QUANTITY_UNITS,
  resolveQuantityUnit,
} from './quantity-units';

describe('quantity units catalog', () => {
  it('matches the shared units contract exactly', () => {
    expect(contract.contract).toBe('quantity-units/1');
    expect(QUANTITY_SCALE_MAX).toBe(contract.maxScale);
    expect(QUANTITY_CUSTOM_DOCUMENT_UNIT).toEqual(contract.customDocumentUnit);
    expect(QUANTITY_UNITS).toEqual(contract.units);
  });

  it('resolves every contract alias to its unit', () => {
    for (const unit of contract.units) {
      for (const alias of unit.aliases) {
        expect(resolveQuantityUnit(alias)?.code, alias).toBe(unit.code);
      }
    }
  });

  it('matches a label after trimming and invariant upper-casing', () => {
    expect(resolveQuantityUnit('  kg ')?.code).toBe('KG');
    expect(resolveQuantityUnit('Kilograms')?.documentUnit).toBe('KGS');
    expect(resolveQuantityUnit('pcs')?.scale).toBe(0);
    expect(resolveQuantityUnit('\tLitre\n')?.code).toBe('L');
  });

  it('leaves custom, empty and missing labels unresolved', () => {
    expect(resolveQuantityUnit('Bundle')).toBeNull();
    expect(resolveQuantityUnit('BAGGIE')).toBeNull();
    expect(resolveQuantityUnit('')).toBeNull();
    expect(resolveQuantityUnit('   ')).toBeNull();
    expect(resolveQuantityUnit(null)).toBeNull();
    expect(resolveQuantityUnit(undefined)).toBeNull();
  });
});
