import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GrantCommand } from './complimentary-grant-api.service';
import { grantDrafts } from './complimentary-grant-drafts';

const command: GrantCommand = {
  operationKey: '22222222-2222-4222-8222-222222222222', shopIds: ['11111111-1111-4111-8111-111111111111'],
  startsAt: '2027-02-28T18:30:00.000000Z', endsAt: '2027-03-31T18:30:00.000000Z', memberAllowance: 3, reason: 'Courtesy',
  expectedVersion: 2, sourceHash: 'b'.repeat(64), confirmed: true,
};

function memoryStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() { return items.size; },
    clear: () => items.clear(),
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => [...items.keys()][index] ?? null,
    removeItem: (key: string) => { items.delete(key); },
    setItem: (key: string, value: string) => { items.set(key, String(value)); },
  };
}

beforeEach(() => vi.stubGlobal('localStorage', memoryStorage()));
afterEach(() => vi.unstubAllGlobals());

describe('complimentary grant drafts', () => {
  it('keeps the exact command per administrator and account', () => {
    expect(grantDrafts.write({ adminId: 'a1', accountId: 'acc-1', command })).toBe(true);
    expect(grantDrafts.read('a1', 'acc-1')?.command).toEqual(command);
    expect(grantDrafts.read('a2', 'acc-1')).toBeNull();
    expect(grantDrafts.read('a1', 'acc-2')).toBeNull();
    grantDrafts.clear({ adminId: 'a1', accountId: 'acc-1', command });
    expect(grantDrafts.read('a1', 'acc-1')).toBeNull();
  });

  it('reports a storage failure instead of pretending the request is recoverable', () => {
    const failing = { setItem: () => { throw new Error('quota'); }, getItem: () => null, removeItem: () => undefined } as unknown as Storage;
    expect(grantDrafts.write({ adminId: 'a1', accountId: 'acc-1', command }, failing)).toBe(false);
  });

  it('keeps concurrent operations for one account independently and clears only the exact operation', () => {
    const a = { adminId: 'a1', accountId: 'acc-1', createdAt: 1, command: { ...command, operationKey: 'op-a' } };
    const b = { adminId: 'a1', accountId: 'acc-1', createdAt: 2, command: { ...command, operationKey: 'op-b' } };
    expect(grantDrafts.write(a)).toBe(true);
    expect(grantDrafts.write(b)).toBe(true);
    expect(grantDrafts.list('a1', 'acc-1').map(item => item.command.operationKey)).toEqual(['op-a', 'op-b']);
    grantDrafts.clear(b);
    expect(grantDrafts.read('a1', 'acc-1')).toEqual(a);
    expect(grantDrafts.holds(a)).toBe(true);
    grantDrafts.clear({ ...a, command: { ...a.command, operationKey: 'op-other' } });
    expect(grantDrafts.read('a1', 'acc-1')).toEqual(a);
  });

  it('acquires only when no other operation is unresolved, yielding to the existing one without a trace', () => {
    const a = { adminId: 'a1', accountId: 'acc-1', createdAt: 1, command: { ...command, operationKey: 'op-a' } };
    const b = { adminId: 'a1', accountId: 'acc-1', createdAt: 2, command: { ...command, operationKey: 'op-b' } };
    expect(grantDrafts.acquire(a)).toEqual(a);
    expect(grantDrafts.acquire(b)).toEqual(a);
    expect(grantDrafts.holds(b)).toBe(false);
    expect(grantDrafts.list('a1', 'acc-1')).toEqual([a]);
  });

  it('still recovers a record saved under the earlier single-slot key, and clears it only by its own operation', () => {
    localStorage.setItem('intelibill.admin.complimentary-grant.v1.a1.acc-1', JSON.stringify({ adminId: 'a1', accountId: 'acc-1', command }));
    const legacy = grantDrafts.read('a1', 'acc-1')!;
    expect(legacy.command).toEqual(command);
    grantDrafts.clear({ ...legacy, command: { ...command, operationKey: 'op-other' } });
    expect(grantDrafts.read('a1', 'acc-1')).not.toBeNull();
    grantDrafts.clear(legacy);
    expect(grantDrafts.read('a1', 'acc-1')).toBeNull();
  });

  it('ignores a record whose owner does not match its key', () => {
    localStorage.setItem('intelibill.admin.complimentary-grant.v1.a1.acc-1', JSON.stringify({ adminId: 'a2', accountId: 'acc-1', command }));
    localStorage.setItem(`intelibill.admin.complimentary-grant.v1.a1.acc-1.${command.operationKey}`,
      JSON.stringify({ adminId: 'a1', accountId: 'acc-1', command: { ...command, operationKey: 'mismatch' } }));
    expect(grantDrafts.read('a1', 'acc-1')).toBeNull();
  });
});
