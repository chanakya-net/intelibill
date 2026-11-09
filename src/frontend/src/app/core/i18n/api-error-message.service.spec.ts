import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { ApiErrorMessageService } from './api-error-message.service';

describe('ApiErrorMessageService', () => {
  const context = {
    fallbackKey: 'sales.failed',
    codeMap: { 'Sale.StockChanged': 'sales.stockChanged' },
    fieldMap: { 'customer.name': 'customerName', quantity: 'quantity' },
  };

  function resolve(problem: unknown) {
    return TestBed.inject(ApiErrorMessageService).resolve(problem, context);
  }

  it('prefers a known code over an untrusted title and caller mappings over common mappings', () => {
    expect(resolve({ error: { code: 'Sale.StockChanged', title: '<b>bad</b>' } }).messageKey).toBe(
      'sales.stockChanged',
    );
    expect(resolve({ code: 'Validation.Failed', title: '<b>bad</b>' }).messageKey).toBe(
      'apiErrors.validationFailed',
    );
  });

  it('accepts only an allowlisted legacy title', () => {
    expect(resolve({ title: 'Operation.PayloadMismatch' }).messageKey).toBe(
      'apiErrors.payloadMismatch',
    );
    expect(resolve({ title: 'Operation.PayloadMismatch<script>' }).messageKey).toBe('sales.failed');
  });

  it('does not let a title override an unknown nonempty code', () => {
    expect(resolve({ code: 'Sale.Unknown', title: 'Validation.Failed' }).messageKey).toBe(
      'sales.failed',
    );
    expect(resolve({ status: 503, error: { code: 'Sale.Unknown' } }).messageKey).toBe(
      'sales.failed',
    );
  });

  it('uses safe transport fallbacks without trusting transport messages', () => {
    expect(resolve({ status: 0, message: '<script>bad</script>' }).messageKey).toBe(
      'apiErrors.networkUnavailable',
    );
    expect(resolve({ status: 503, error: { title: 'raw error' } }).messageKey).toBe(
      'apiErrors.serviceUnavailable',
    );
  });

  it('drops arbitrary prose and handles missing or non-object bodies', () => {
    for (const problem of [
      null,
      42,
      'bad',
      { detail: '<script>bad</script>', description: 'bad', message: 'bad', title: 'bad' },
    ]) {
      expect(resolve(problem)).toEqual({ messageKey: 'sales.failed', params: {}, fieldErrors: [] });
    }
  });

  it('resolves only mapped fields from dictionary errors without using descriptions', () => {
    expect(
      resolve({ errors: { 'customer.name': ['<img>'], unknown: ['bad'], quantity: ['bad'] } })
        .fieldErrors,
    ).toEqual([
      { field: 'customerName', messageKey: 'apiErrors.invalidField', params: {} },
      { field: 'quantity', messageKey: 'apiErrors.invalidField', params: {} },
    ]);
  });

  it('resolves recognized array code paths and ignores unknown descriptions', () => {
    expect(
      resolve({
        errors: [
          { code: 'quantity', description: '<img>' },
          { code: 'unknown', description: 'bad' },
        ],
      }).fieldErrors,
    ).toEqual([{ field: 'quantity', messageKey: 'apiErrors.invalidField', params: {} }]);
  });

  it('exposes only bounded safe trace identifiers', () => {
    expect(resolve({ error: { traceId: 'abc._:-123' } }).correlationId).toBe('abc._:-123');
    for (const traceId of ['', 'unsafe <script>', 'x'.repeat(129), 123]) {
      expect(resolve({ traceId }).correlationId).toBeUndefined();
    }
  });

  it('leaves the source problem and context unchanged', () => {
    const problem = Object.freeze({ errors: Object.freeze({ quantity: Object.freeze(['raw']) }) });
    const before = JSON.stringify(problem);
    resolve(problem);
    expect(JSON.stringify(problem)).toBe(before);
  });
});
