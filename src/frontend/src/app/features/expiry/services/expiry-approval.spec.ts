import { describe, expect, it } from 'vitest';
import { buildExpiryApprovals } from './expiry-approval';
import type { SaleExpiryLineDecision } from './expiry.models';
const line: SaleExpiryLineDecision = {
  itemId: 'item',
  inventoryBatchId: 'batch',
  quantity: 0.1,
  batchVersion: 8,
  decision: {
    allowed: true,
    reasonCode: 'Expiry.Expired',
    warning: true,
    requiresApproval: true,
    policyVersion: 2,
    catalogVersion: 3,
    businessDate: '2026-11-08',
    physicalQuantity: 2,
    quarantinedQuantity: 0,
    availableQuantity: 2,
  },
};
describe('warning approval binding', () => {
  it('binds fractional duplicate lines as exactly 0.3 with batch and policy versions', () => {
    expect(
      buildExpiryApprovals(
        [line, { ...line, quantity: 0.2 }],
        [line, { ...line, quantity: 0.2 }],
        true,
        ' Inspected ',
      ),
    ).toEqual([
      {
        inventoryBatchId: 'batch',
        quantity: 0.3,
        batchVersion: 8,
        policyVersion: 2,
        catalogVersion: 3,
        reason: 'Inspected',
      },
    ]);
  });
  it('refuses staff, empty reason, changed quantity and inconsistent batch versions', () => {
    expect(buildExpiryApprovals([line], [line], false, 'Inspected')).toBeNull();
    expect(buildExpiryApprovals([line], [line], true, ' ')).toBeNull();
    expect(
      buildExpiryApprovals([line], [{ ...line, quantity: 0.2 }], true, 'Inspected'),
    ).toBeNull();
    expect(
      buildExpiryApprovals([line, { ...line, batchVersion: 9 }], [line, line], true, 'Inspected'),
    ).toBeNull();
  });
  it('never approves a hard block', () => {
    expect(
      buildExpiryApprovals(
        [{ ...line, decision: { ...line.decision, allowed: false } }],
        [line],
        true,
        'Inspected',
      ),
    ).toBeNull();
  });
});
