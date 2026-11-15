import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { AffiliationDrafts } from './affiliation-drafts';
import { AffiliationOperation } from './affiliation-operation';

describe('affiliation operations in two tabs', () => {
  const scope = { userId: 'owner', shopId: 'shop', epoch: 1 };
  const consentKey = ['owner', 'account', 'shop', 'request', 'consent'];
  const samePage = () => true;
  const completed = () => undefined;
  const unavailable = () => throwError(() => new HttpErrorResponse({ status: 503 }));

  let firstTab: AffiliationOperation;
  let secondTab: AffiliationOperation;
  let storage: AffiliationDrafts;

  beforeEach(() => {
    stubMemoryStorage();
    TestBed.configureTestingModule({
      providers: [{ provide: ShopContextService, useValue: { isCurrent: () => true, epoch: () => 1 } }],
    });
    firstTab = TestBed.runInInjectionContext(() => new AffiliationOperation());
    secondTab = TestBed.runInInjectionContext(() => new AffiliationOperation());
    storage = TestBed.inject(AffiliationDrafts);
  });

  it.each([
    ['accept', 'revoke'],
    ['revoke', 'accept'],
  ] as const)('keeps a pending %s when a Granted tab clicks %s', (saved, clicked) => {
    firstTab.restore(consentKey);
    secondTab.restore(consentKey);
    firstTab.submit(scope, consentKey, { newDraft: { decision: saved } }, unavailable, samePage, completed);
    const frozen = firstTab.pending();
    expect(frozen).toMatchObject({ decision: saved, operationKey: expect.any(String) });
    expect(secondTab.pending()).toBeNull();

    const send = vi.fn(() => of({}));
    secondTab.submit(scope, consentKey, { newDraft: { decision: clicked } }, send, samePage, completed);

    expect(send).not.toHaveBeenCalled();
    expect(secondTab.pending()).toEqual(frozen);
    expect(storage.read(consentKey)).toEqual(frozen);

    secondTab.submit(scope, consentKey, { retryKey: frozen!.operationKey }, send, samePage, completed);
    expect(send).toHaveBeenCalledExactlyOnceWith(frozen);
    expect(storage.read(consentKey)).toBeNull();
  });

  it.each([
    { shopId: 'different-shop', termId: 'grouped', withoutCoverage: false },
    { shopId: 'shop', termId: null, withoutCoverage: true },
  ])('preserves the pending account request when another tab submits %j', (different) => {
    const payer = { ...scope, userId: 'payer', shopId: '' };
    const key = ['payer', 'account', 'request'];
    const original = { shopId: 'shop', termId: 'grouped', withoutCoverage: false };
    firstTab.submit(payer, key, { newDraft: original }, unavailable, samePage, completed);
    const frozen = firstTab.pending();

    const send = vi.fn(() => of({}));
    secondTab.submit(payer, key, { newDraft: different }, send, samePage, completed);

    expect(send).not.toHaveBeenCalled();
    expect(secondTab.pending()).toEqual(frozen);
    expect(storage.read(key)).toEqual(frozen);

    secondTab.submit(payer, key, { retryKey: frozen!.operationKey }, send, samePage, completed);
    expect(send).toHaveBeenCalledExactlyOnceWith(frozen);
  });

  it('does not send a newer saved decision when the displayed retry has already settled', () => {
    const { draft } = storage.freeze(consentKey, { decision: 'accept' });
    firstTab.restore(consentKey);
    storage.settle(consentKey, draft.operationKey);
    const { draft: newer } = storage.freeze(consentKey, { decision: 'revoke' });

    const send = vi.fn(() => of({}));
    firstTab.submit(scope, consentKey, { retryKey: draft.operationKey }, send, samePage, completed);

    expect(send).not.toHaveBeenCalled();
    expect(firstTab.pending()).toEqual(newer);
    expect(storage.read(consentKey)).toEqual(newer);
  });
});
