import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { ScopeMismatchError, StaleScopeError } from '../../../core/context/scoped-request';
import {
  classifyReceivingFailure,
  normalizeReceivingErrors,
  receivingMessage,
} from './receiving-errors';
const payload = { lines: [{ clientLineId: 'a' }, { clientLineId: 'b' }] };
describe('receiving error adapter', () => {
  it('maps SafeErrorFactory field arrays to frozen receiving identities without descriptions', () => {
    expect(
      normalizeReceivingErrors(
        {
          errors: [
            { code: 'Lines[1].Quantity', description: '<script>raw</script>' },
            { code: 'Unknown.Field', description: 'raw' },
          ],
        } as never,
        payload as never,
      ),
    ).toEqual([
      {
        clientLineId: 'b',
        field: 'quantity',
        code: 'Lines[1].Quantity',
        messageKey: 'purchaseOrders.receiving.invalidQuantity',
      },
    ]);
  });
  it('maps validator paths and handler metadata to frozen client identities without reading human messages', () => {
    const errors = normalizeReceivingErrors(
      {
        errors: { 'Lines[1].Quantity': ['arbitrary human text'] },
        rowErrors: [
          { lineIndex: 0, code: 'Inventory.BatchNumberAlreadyExists', field: 'BatchNumber' },
        ],
      },
      payload as never,
    );
    expect(errors).toEqual([
      expect.objectContaining({
        clientLineId: 'a',
        field: 'batchNumber',
        messageKey: 'purchaseOrders.errors.batchNumberAlreadyExists',
      }),
      expect.objectContaining({
        clientLineId: 'b',
        field: 'quantity',
        messageKey: 'purchaseOrders.receiving.invalidQuantity',
      }),
    ]);
  });
  it.each([0, 408, 429, 500, 503])('keeps transport/status %s unknown', (status) =>
    expect(classifyReceivingFailure(new HttpErrorResponse({ status }), false)).toBe('unknown'),
  );
  it('retains payload mismatch and already-unknown authorization/scope failures', () => {
    expect(
      classifyReceivingFailure(
        new HttpErrorResponse({ status: 409, error: { code: 'Operation.PayloadMismatch' } }),
        false,
      ),
    ).toBe('unknown');
    for (const error of [
      new HttpErrorResponse({ status: 401 }),
      new HttpErrorResponse({ status: 403 }),
      new ScopeMismatchError(),
      new StaleScopeError(),
    ])
      expect(classifyReceivingFailure(error, true)).toBe('unknown');
    expect(classifyReceivingFailure(new HttpErrorResponse({ status: 400 }), true)).toBe('rejected');
  });
  it.each([
    ['Licence.Expired', 403, 'expired', 'rejected'],
    ['Licence.VerificationUnavailable', 503, 'unavailable', 'rejected'],
    ['Licence.Busy', 503, 'busy', 'unknown'],
  ] as const)(
    'maps %s safely and preserves a previously uncertain receipt',
    (code, status, key, initial) => {
      const error = new HttpErrorResponse({ status, error: { code, detail: 'Untrusted' } });
      expect(receivingMessage(code)).toBe(`billing.status.tradingAuthority.${key}`);
      expect(classifyReceivingFailure(error, false)).toBe(initial);
      expect(classifyReceivingFailure(error, true)).toBe('unknown');
    },
  );

  it('preserves a receipt when the guarded handoff answers with a security refusal', () => {
    const error = new HttpErrorResponse({
      status: 403,
      error: { code: 'Licence.SecuritySuspended' },
    });
    expect(classifyReceivingFailure(error, false)).toBe('unknown');
    expect(receivingMessage('Licence.SecuritySuspended')).toBe(
      'billing.status.suspension.enforcedSecurity',
    );
  });
  it('keeps a 404 retry unknown because it cannot disprove an earlier commit', () => {
    expect(classifyReceivingFailure(new HttpErrorResponse({ status: 404 }), true)).toBe('unknown');
  });
});
