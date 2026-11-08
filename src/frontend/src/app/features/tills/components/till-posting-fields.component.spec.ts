import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { beforeEach, afterEach, describe, it, expect } from 'vitest';
import { TillPostingFieldsComponent } from './till-posting-fields.component';
import { TillContextService } from '../services/till-context.service';
const owner = { userId: 'u', shopId: 's' };
describe('TillPostingFieldsComponent evidence', () => {
  let fields: TillPostingFieldsComponent;
  const enabled = signal(false),
    cap = signal<unknown>({ enabled: false }),
    error = signal(false);
  beforeEach(() => {
    enabled.set(false);
    cap.set({ enabled: false });
    error.set(false);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: TillContextService,
          useValue: {
            enabled,
            capability: cap,
            error,
            owner: () => owner,
            posting: () => (enabled() ? { till: { sessionId: 'd' } } : null),
          },
        },
      ],
    });
    fields = TestBed.runInInjectionContext(() => new TillPostingFieldsComponent());
  });
  afterEach(() => TestBed.resetTestingModule());
  it('retains owner metadata when disabled so a prior frozen request is recovered before legacy posting', () => {
    expect(fields.fields(10)).toEqual({ tillOwner: owner });
    expect(fields.sessionOnly()).toEqual({ tillOwner: owner });
  });
  it('unknown capabilities cannot manufacture disabled evidence', () => {
    cap.set(null);
    expect(() => fields.fields(10)).toThrow();
  });
  it('does not infer cash for a new enabled expense', () => {
    enabled.set(true);
    expect(() => fields.fields(10)).toThrow();
  });
  it('requires exact nonzero correction delta and explicit no movement for metadata correction', () => {
    enabled.set(true);
    fields.evidence = true;
    fields.disposition = 'MoneyReceived';
    fields.reason = 'Recovered difference';
    fields.method = 'Upi';
    expect(fields.fields(12.34)).toEqual({
      tillOwner: owner,
      till: { sessionId: 'd' },
      settlementEvidence: {
        disposition: 'MoneyReceived',
        reason: 'Recovered difference',
        tender: { method: 'Upi', appliedAmount: 12.34, externalReference: null },
      },
    });
    expect(() => fields.fields(0)).toThrow();
    fields.disposition = 'NoPhysicalMovement';
    expect(fields.fields(0)).toEqual({
      tillOwner: owner,
      settlementEvidence: { disposition: 'NoPhysicalMovement', reason: 'Recovered difference' },
    });
  });
  it('keeps disabled external refund owner metadata for pending recovery', () => {
    expect(fields.refundFields(10, true)).toEqual({ tillOwner: owner });
  });
  it('requires explicit confirmation before an enabled external refund can be frozen', () => {
    enabled.set(true);
    fields.method = 'Cash';
    expect(() => fields.refundFields(10, true)).toThrow();
    fields.refundConfirmed = true;
    expect(fields.refundFields(10, true)).toMatchObject({
      refundTender: { method: 'Cash', appliedAmount: 10 },
      refundConfirmed: true,
    });
  });
});
