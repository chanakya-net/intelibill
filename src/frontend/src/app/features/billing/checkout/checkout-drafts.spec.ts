import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubMemoryStorage } from '../billing.test-utils';
import { CheckoutDrafts } from './checkout-drafts';
import type { AcceptQuote, CreateQuote } from './checkout.models';

const id = (value: number) => '00000000-0000-4000-8000-00000000000' + value;
const owner = { userId: id(1), shopId: '', account: id(2) };
const order = id(3);
const create: CreateQuote = {
  operationKey: id(4), planVersionId: id(5), period: 'Month', shopIds: [id(6)],
  extraShops: 0, expectedRevision: 1, termId: null,
};
const accept: AcceptQuote = {
  operationKey: id(7), expectedRevision: 1, snapshotHash: 'a'.repeat(64),
  customerTermsVersion: 'fixture-1', refundPolicyVersion: 'fixture-2', confirmed: true,
};

let originalLocks: PropertyDescriptor | undefined;

function stubLocks(locks: { request: (key: string, ...args: never[]) => Promise<unknown> } | undefined) {
  Object.defineProperty(navigator, 'locks', { configurable: true, value: locks });
}

/** One browser's lock manager: exclusive per name, and `ifAvailable` answers null while the name is held. */
function sharedLocks() {
  const queues = new Map<string, Promise<unknown>>();
  const held = new Set<string>();
  stubLocks({ request: (name: string, ...args: unknown[]) => {
    const run = args.pop() as (lock: { name: string } | null) => unknown;
    if ((args[0] as { ifAvailable?: boolean } | undefined)?.ifAvailable && held.has(name))
      return Promise.resolve().then(() => run(null));
    const result = (queues.get(name) ?? Promise.resolve()).then(async () => {
      held.add(name);
      try {
        return await run({ name });
      } finally {
        held.delete(name);
      }
    });
    queues.set(name, result.catch(() => undefined));
    return result;
  } });
}

beforeEach(() => { originalLocks = Object.getOwnPropertyDescriptor(navigator, 'locks'); });
afterEach(() => {
  if (originalLocks) Object.defineProperty(navigator, 'locks', originalLocks);
  else Reflect.deleteProperty(navigator, 'locks');
  vi.unstubAllGlobals();
});

describe('CheckoutDrafts', () => {
  it('retains an uncertain selection across tabs and binds it to the billing account and actor', async () => {
    stubMemoryStorage();
    sharedLocks();
    const first = new CheckoutDrafts();
    const reopened = new CheckoutDrafts();
    const different = { ...create, operationKey: id(8), period: 'Year' as const };
    const original = { command: create, order: null };
    expect(await Promise.all([first.prepareCreate(owner, create), reopened.prepareCreate(owner, different)]))
      .toEqual([original, original]);
    await first.startOver(owner, order);
    expect(reopened.readCreate(owner)).toEqual(original);
    expect(reopened.readCreate({ ...owner, account: id(8) })).toBeNull();
    expect(reopened.readCreate({ ...owner, userId: id(8) })).toBeNull();
    await first.finishCreate(owner, create.operationKey, order);
    expect(reopened.readCreate(owner)).toEqual({ ...original, order });
  });

  it('keeps one exact confirmation per order and preserves the settled receipt across tabs', async () => {
    stubMemoryStorage();
    sharedLocks();
    const first = new CheckoutDrafts();
    const second = new CheckoutDrafts();
    const another = { ...accept, operationKey: id(8) };
    const saved = { command: accept, settled: false };
    expect(await Promise.all([first.prepareAccept(owner, order, accept), second.prepareAccept(owner, order, another)]))
      .toEqual([saved, saved]);
    expect(second.readAccept(owner, id(8))).toBeNull();
    await second.finishAccept(owner, order, another.operationKey, false);
    expect(first.readAccept(owner, order)).toEqual(saved);
    await first.finishAccept(owner, order, accept.operationKey, true);
    expect(await second.prepareAccept(owner, order, another)).toEqual({ ...saved, settled: true });
  });

  it('shares one frozen payment command and retains a terminal tombstone across tabs', async () => {
    stubMemoryStorage();
    sharedLocks();
    const first = new CheckoutDrafts();
    const second = new CheckoutDrafts();
    const [original, adopted] = await Promise.all([
      first.preparePayment(owner, order, 1, 'a'.repeat(64)),
      second.preparePayment(owner, order, 2, 'b'.repeat(64)),
    ]);
    expect(adopted).toEqual(original);
    expect(original.command?.expectedRevision).toBe(1);
    expect(second.readPayment({ ...owner, account: id(8) }, order)).toBeNull();
    expect(second.readPayment({ ...owner, userId: id(8) }, order)).toBeNull();
    const attempt = id(9);
    await first.recordPayment(owner, order, attempt, 'pending');
    await second.recordPayment(owner, order, attempt, 'verified');
    await expect(first.recordPayment(owner, order, attempt, 'awaiting')).resolves.toMatchObject({
      attempt, command: original.command, state: 'verified',
    });
    await expect(first.recordPayment(owner, order, id(8), 'pending')).rejects.toThrow('Checkout attempt changed');
  });

  it('advances the fence for a new claim on a pending draft but not for a routine pending read', async () => {
    stubMemoryStorage();
    sharedLocks();
    const first = new CheckoutDrafts();
    const second = new CheckoutDrafts();
    await first.preparePayment(owner, order, 1, 'a'.repeat(64));
    const attempt = id(9);
    await first.recordPayment(owner, order, attempt, 'pending');
    const fence = first.paymentRevision(owner, order);

    await second.recordPayment(owner, order, attempt, 'pending');
    expect(second.paymentRevision(owner, order)).toBe(fence);
    await second.recordPayment(owner, order, attempt, 'pending', undefined, true);
    expect(second.paymentRevision(owner, order)).toBe(fence + 1);

    await expect(first.recordPayment(owner, order, attempt, 'awaiting', fence)).resolves.toMatchObject({ state: 'pending' });
    await expect(first.recordPayment(owner, order, attempt, 'awaiting', fence + 1)).resolves.toMatchObject({ state: 'awaiting' });
  });

  it('never reopens a pending attempt while a page holds its hosted window open', async () => {
    stubMemoryStorage();
    sharedLocks();
    const opener = new CheckoutDrafts();
    const other = new CheckoutDrafts();
    await opener.preparePayment(owner, order, 1, 'a'.repeat(64));
    const attempt = id(9);
    const release = await opener.holdWindow(owner, order);
    expect(release).toBeInstanceOf(Function);
    expect(await other.holdWindow(owner, order)).toBeNull();
    await opener.recordPayment(owner, order, attempt, 'pending', undefined, true);
    const fence = other.paymentRevision(owner, order);

    await expect(other.recordPayment(owner, order, attempt, 'awaiting', fence)).resolves.toMatchObject({ state: 'pending' });
    release!();
    await expect(other.recordPayment(owner, order, attempt, 'awaiting', fence)).resolves.toMatchObject({ state: 'awaiting' });
  });

  it('fails to prepare a request when durable storage or locking is unavailable', async () => {
    const drafts = new CheckoutDrafts();
    stubMemoryStorage();
    stubLocks(undefined);
    await expect(drafts.prepareCreate(owner, create)).rejects.toThrow();
    sharedLocks();
    vi.spyOn(localStorage, 'setItem').mockImplementation(() => { throw new Error('Storage is full'); });
    await expect(drafts.prepareAccept(owner, order, accept)).rejects.toThrow('Storage is full');
    expect(drafts.readAccept(owner, order)).toBeNull();
  });
});
