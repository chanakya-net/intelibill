import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { normalizeRefundIntakeReason, RefundIntakeApi, type RefundIntakeItem } from './refund-intake-api.service';

interface ReasonVector { readonly name: string; readonly input: string; readonly normalized: string | null }

// The licensing contract test reads these same vectors against RefundRequestsV1.NormalizeReason.
const contract = JSON.parse(readFileSync(
  resolve(process.cwd(), '../../../tests/contracts/licensing/refund-request-v1.json'), 'utf8'),
) as { reasonNormalization: { vectors: ReasonVector[] } };
const vectors = contract.reasonNormalization.vectors;
const account = '11111111-aaaa-4111-8111-1111111111bb';
const markedReason = '﻿Duplicate shop setup';

function intakeRow(index: number, reason: string): RefundIntakeItem {
  return {
    refundRequestId: `66666666-6666-4666-8666-${String(index).padStart(12, '0')}`,
    accountId: account,
    orderId: '33333333-3333-4333-8333-333333333333',
    confirmationId: '44444444-4444-4444-8444-444444444444',
    actorId: '55555555-5555-4555-8555-555555555555',
    paymentSource: 'Online', status: 'Requested', amountPaise: 4500, paymentAmountPaise: 12000, currency: 'INR',
    reason, requestedAt: '2028-01-31T04:30:00Z',
    providerId: 'development', providerAccountId: 'dev-account', environment: 'Development', configurationVersion: 'sim/1',
  };
}

async function listWith(rows: readonly RefundIntakeItem[]) {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const answer = TestBed.inject(RefundIntakeApi).list(account, 'session-1');
  TestBed.inject(HttpTestingController).expectOne(`/api/admin/accounts/${account}/refund-requests`)
    .flush({ schemaVersion: 1, accountId: account, items: rows, nextCursor: null });
  return answer;
}

afterEach(() => TestBed.resetTestingModule());

describe('admin refund intake reason follows the V1 server contract', () => {
  it('includes the recorded leading U+FEFF vector', () => {
    expect(vectors.find(vector => vector.input === markedReason)?.normalized).toBe(markedReason);
  });

  it.each(vectors)('normalizes "$name" as the server does', vector => {
    expect(normalizeRefundIntakeReason(vector.input)).toBe(vector.normalized);
  });

  it('loads a page holding a U+FEFF reason together with its neighbours', async () => {
    const page = await listWith([
      intakeRow(1, 'Neighbour before'), intakeRow(2, markedReason), intakeRow(3, 'Neighbour after'),
    ]);
    expect(page.items.map(item => item.reason)).toEqual(['Neighbour before', markedReason, 'Neighbour after']);
  });

  it.each(vectors.filter(vector => vector.normalized !== vector.input))(
    'refuses a page whose row text "$name" the server would never record', async vector => {
      await expect(listWith([intakeRow(1, vector.input)])).rejects.toThrow('Invalid refund intake page');
    });
});
