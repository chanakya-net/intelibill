import { describe, expect, it } from 'vitest';

import {
  createGuardedFetch,
  createRecordTracker,
  redactHeaders,
  requireLiveConfig,
  redactLiveValues,
} from '../support/live-api.fixture';

describe('live API fixture safety', () => {
  it('missing secrets are redacted', () => {
    const secret = 'runtime-only-password';
    let message = '';

    try {
      requireLiveConfig({
        INTELIBILL_LIVE_PASSWORD: secret,
      });
    } catch (error) {
      message = error instanceof Error ? error.message : String(error);
    }

    expect(message).toBe('Missing live UI audit configuration: INTELIBILL_LIVE_USERNAME');
    expect(message).not.toContain(secret);
    expect(redactLiveValues(`password=${secret}`, [secret])).toBe('password=[REDACTED]');
    expect(redactHeaders({ authorization: `Bearer ${secret}` }, [secret])).toEqual({
      authorization: '[REDACTED]',
    });
  });

  it('unprefixed cleanup is rejected', () => {
    const tracker = createRecordTracker('ui-audit-run-123');
    tracker.track({
      type: 'item',
      id: 'tracked-id',
      name: 'ui-audit-run-123-item',
    });

    expect(() =>
      tracker.assertCleanupAllowed({
        type: 'item',
        id: 'tracked-id',
        name: 'unrelated-item',
      }),
    ).toThrowError('Cleanup rejected: record lacks current run prefix');
    expect(() =>
      tracker.assertCleanupAllowed({
        type: 'item',
        id: 'untracked-id',
        name: 'ui-audit-run-123-item',
      }),
    ).toThrowError('Cleanup rejected: record was not tracked by current run');
  });

  it('reset and bulk-clear requests cannot be issued', async () => {
    const issuedRequests: string[] = [];
    const guardedFetch = createGuardedFetch(async (input) => {
      issuedRequests.push(String(input));
      return new Response(null, { status: 204 });
    }, 'http://localhost:5277/api');

    await expect(
      guardedFetch('http://localhost:5277/api/admin/reset', {
        method: 'POST',
      }),
    ).rejects.toThrowError('Unsafe live API request rejected');
    await expect(
      guardedFetch('http://localhost:5277/api/items/bulk-clear', {
        method: 'POST',
      }),
    ).rejects.toThrowError('Unsafe live API request rejected');
    await expect(
      guardedFetch('http://localhost:5277/api/items', { method: 'DELETE' }),
    ).rejects.toThrowError('Bulk DELETE request rejected');
    expect(issuedRequests).toEqual([]);
  });
});
