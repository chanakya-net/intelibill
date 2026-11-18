import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { ManualPaymentApi } from './manual-payment-api.service';
import { PaymentConfirmationsComponent } from './payment-confirmations.component';

afterEach(() => TestBed.resetTestingModule());

describe('provider confirmation scope', () => {
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
