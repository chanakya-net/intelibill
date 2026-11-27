import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { stubMemoryStorage } from '../billing.test-utils';
import { RefundRequestDrafts } from './refund-request-drafts';
import { isValidRefundRequest, normalizeRefundReason, validateRefundPage } from './refund-request.models';
import {
  account, configureRefundPage, confirmation, draftScope, installRefundSpecGlobals, mountRefundPage, order,
  restoreRefundSpecGlobals,
  refundRow, saveDraft, savedKey, storageKey,
} from './refund-request.test-utils';

interface ReasonVector { readonly name: string; readonly input: string; readonly normalized: string | null }

// The licensing contract test reads these same vectors against RefundRequestsV1.NormalizeReason.
const contract = JSON.parse(readFileSync(
  resolve(process.cwd(), '../../../tests/contracts/licensing/refund-request-v1.json'), 'utf8'),
) as { reasonNormalization: { vectors: ReasonVector[] } };
const vectors = contract.reasonNormalization.vectors;
const markedReason = '﻿Duplicate shop setup';

beforeEach(() => {
  stubMemoryStorage();
  installRefundSpecGlobals();
});
afterEach(() => {
  restoreRefundSpecGlobals();
  vi.unstubAllGlobals();
  TestBed.resetTestingModule();
});

describe('merchant refund reason follows the V1 server contract', () => {
  it('includes the recorded leading U+FEFF vector', () => {
    expect(vectors.find(vector => vector.input === markedReason)?.normalized).toBe(markedReason);
  });

  it.each(vectors)('normalizes "$name" as the server does', vector => {
    expect(normalizeRefundReason(vector.input)).toBe(vector.normalized);
  });

  it.each(vectors.filter(vector => vector.normalized !== null))(
    'reads a row recorded from "$name" and refuses one that was never normalized', vector => {
      const recorded = refundRow({ amountPaise: 4500, reason: vector.normalized! });
      expect(isValidRefundRequest(recorded, account)).toBe(true);
      if (vector.input !== vector.normalized)
        expect(isValidRefundRequest({ ...recorded, reason: vector.input }, account)).toBe(false);
    });

  it('loads a page holding a U+FEFF reason together with its neighbours', () => {
    const rows = [
      refundRow({ amountPaise: 1000, reason: 'Neighbour before' }, 1),
      refundRow({ amountPaise: 4500, reason: markedReason }, 2),
      refundRow({ amountPaise: 2000, reason: 'Neighbour after' }, 3),
    ];
    const page = validateRefundPage({ schemaVersion: 1, accountId: account, items: rows, nextCursor: null }, account);
    expect(page.items.map(item => item.reason)).toEqual(['Neighbour before', markedReason, 'Neighbour after']);
  });

  it('recovers a persisted draft whose reason begins with U+FEFF using its saved key and exact text', async () => {
    saveDraft(markedReason);
    const { create } = configureRefundPage({ create: async (_scope, command) => refundRow(command) });
    expect(TestBed.inject(RefundRequestDrafts).read(draftScope, confirmation, order)?.command.reason).toBe(markedReason);

    const reloaded = mountRefundPage();
    await reloaded.settle();

    expect(create).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][1]).toEqual({
      operationKey: savedKey, orderId: order, confirmationId: confirmation, amountPaise: 4500, reason: markedReason,
    });
    expect(reloaded.find('refund-request-unreadable')).toBeNull();
    expect(reloaded.find('refund-request-row-reason')?.textContent).toBe(markedReason);
    expect(localStorage.getItem(storageKey)).toBeNull();
  });
});
