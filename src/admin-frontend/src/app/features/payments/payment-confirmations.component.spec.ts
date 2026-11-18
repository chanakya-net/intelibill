import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { ManualPaymentApi } from './manual-payment-api.service';
import { PaymentConfirmationsComponent } from './payment-confirmations.component';

afterEach(() => TestBed.resetTestingModule());

describe('provider confirmation scope', () => {
  it('loads every page and retains the oldest download link', async () => {
    const rows = Array.from({ length: 51 }, (_, i) => ({ confirmationId: `receipt-${i}`, source: 'Manual' }));
    const list = vi.fn(async (_scope: unknown, _session: string, after?: string) => {
      const start = after ? rows.findIndex(row => row.confirmationId === after) + 1 : 0;
      return rows.slice(start, start + 25);
    });
    TestBed.configureTestingModule({ providers: [
      { provide: AdminSessionService, useValue: { adminId: signal('admin'), sessionId: signal('session'), capabilities: signal(['billing']) } },
      { provide: ManualPaymentApi, useValue: { confirmations: list } },
    ] });
    const fixture = TestBed.createComponent(PaymentConfirmationsComponent);
    fixture.componentRef.setInput('account', 'account');
    fixture.componentRef.setInput('order', 'order');
    fixture.detectChanges();
    for (const id of ['admin-confirmation-list', 'admin-confirmation-more', 'admin-confirmation-more']) {
      fixture.nativeElement.querySelector(`[data-testid="${id}"]`).click();
      await fixture.whenStable();
      fixture.detectChanges();
    }
    expect(fixture.nativeElement.querySelectorAll('[data-testid="admin-confirmation-download"]')).toHaveLength(51);
    expect(list.mock.calls.map(call => call[2])).toEqual([undefined, 'receipt-24', 'receipt-49']);
    expect(fixture.nativeElement.querySelector('[data-testid="admin-confirmation-more"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('receipt-0');
  });

  it('uses the live admin session and discards an attachment after it changes', async () => {
    const session = signal('session-one');
    let complete!: (blob: Blob) => void;
    const document = vi.fn(() => new Promise<Blob>(resolve => { complete = resolve; }));
    const list = vi.fn(async () => [{ confirmationId: 'receipt', source: 'Manual' }]);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    TestBed.configureTestingModule({ providers: [
      { provide: AdminSessionService, useValue: { adminId: signal('admin'), sessionId: session, capabilities: signal(['billing']) } },
      { provide: ManualPaymentApi, useValue: { confirmations: list, confirmationDocument: document } },
    ] });
    const fixture = TestBed.createComponent(PaymentConfirmationsComponent);
    fixture.componentRef.setInput('account', 'account');
    fixture.componentRef.setInput('order', 'order');
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="admin-confirmation-list"]').click();
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="admin-confirmation-download"]').click();
    expect(document).toHaveBeenCalledWith(expect.objectContaining({ account: 'account', order: 'order', admin: 'admin' }), 'session-one', 'receipt');
    session.set('session-two');
    fixture.detectChanges();
    complete(new Blob(['private'], { type: 'text/plain' }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(click).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="admin-confirmation-download"]')).toBeNull();
    click.mockRestore();
  });
});
