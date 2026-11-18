import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { BlobDownloadService } from '../../../shared/utils/blob-download.service';
import { PaymentConfirmationLinksComponent } from './payment-confirmation-links.component';
import { PaymentConfirmationService } from './payment-confirmation.service';
import { confirmations } from './payment-confirmation.models';

const account = '00000000-0000-4000-8000-000000000001';
const order = '00000000-0000-4000-8000-000000000002';
const id = '00000000-0000-4000-8000-000000000003';
const row = { confirmationId: id, accountId: account, orderId: order, source: 'Online' as const,
  amountPaise: 12000, currency: 'INR' as const, verifiedAt: '2027-02-10T00:00:01Z', issuedAt: '2027-02-10T00:00:01Z', issuanceBasis: 'Verification' as const };
afterEach(() => TestBed.resetTestingModule());

function setup(document: () => Promise<Blob>, list = async (_scope?: unknown, _order?: string, _after?: string) => [row]) {
  const scope = signal<{ userId: string; shopId: string } | null>({ userId: 'payer', shopId: 'shop' });
  const download = vi.fn();
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({ langs: { 'en-IN': en },
      translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true })],
    providers: [
      { provide: ShopContextService, useValue: { scope } },
      { provide: PaymentConfirmationService, useValue: { list, document } },
      { provide: BlobDownloadService, useValue: { download } },
    ],
  });
  const fixture = TestBed.createComponent(PaymentConfirmationLinksComponent);
  fixture.componentRef.setInput('account', account);
  fixture.componentRef.setInput('order', order);
  fixture.detectChanges();
  const click = (id: string) => fixture.nativeElement.querySelector(`[data-testid="${id}"]`).click();
  const settle = async () => { await fixture.whenStable(); fixture.detectChanges(); };
  return { fixture, click, settle, scope, download };
}

describe('immutable confirmation downloads', () => {
  it('downloads authenticated bytes with a fixed safe filename', async () => {
    const blob = new Blob(['PAYMENT CONFIRMATION'], { type: 'text/plain' });
    const page = setup(async () => blob);
    page.click('confirmation-list');
    await page.settle();
    page.click('confirmation-download');
    await page.settle();
    expect(page.download).toHaveBeenCalledExactlyOnceWith(blob, `payment-confirmation-${id}.txt`);
  });

  it('traverses bounded pages without hiding earlier downloads', async () => {
    const rows = Array.from({ length: 51 }, (_, i) => ({ ...row,
      confirmationId: `00000000-0000-4000-8000-${String(i + 10).padStart(12, '0')}` }));
    const list = vi.fn(async (_scope?: unknown, _order?: string, after?: string) => {
      const start = after ? rows.findIndex(row => row.confirmationId === after) + 1 : 0;
      return rows.slice(start, start + 25);
    });
    const page = setup(async () => new Blob(['receipt']), list);
    page.click('confirmation-list');
    await page.settle();
    expect(page.fixture.nativeElement.querySelectorAll('[data-testid="confirmation-download"]')).toHaveLength(25);
    page.click('confirmation-more');
    await page.settle();
    page.click('confirmation-more');
    await page.settle();
    expect(page.fixture.nativeElement.querySelectorAll('[data-testid="confirmation-download"]')).toHaveLength(51);
    expect(list.mock.calls.map(call => call[2])).toEqual([undefined, rows[24].confirmationId, rows[49].confirmationId]);
    expect(page.fixture.nativeElement.querySelector('[data-testid="confirmation-more"]')).toBeNull();
    page.click('confirmation-download');
    await page.settle();
    expect(page.download).toHaveBeenCalledWith(expect.any(Blob), `payment-confirmation-${rows[0].confirmationId}.txt`);
  });

  it('drops a download that completes after the owner scope changes', async () => {
    let complete!: (blob: Blob) => void;
    const page = setup(() => new Promise(resolve => { complete = resolve; }));
    page.click('confirmation-list');
    await page.settle();
    page.click('confirmation-download');
    page.scope.set({ userId: 'other', shopId: 'other-shop' });
    page.fixture.detectChanges();
    complete(new Blob(['private']));
    await page.settle();
    expect(page.download).not.toHaveBeenCalled();
    expect(page.fixture.nativeElement.querySelector('[data-testid="confirmation-download"]')).toBeNull();
  });

  it('drops a download after its component is destroyed', async () => {
    let complete!: (blob: Blob) => void;
    const page = setup(() => new Promise(resolve => { complete = resolve; }));
    page.click('confirmation-list');
    await page.settle();
    page.click('confirmation-download');
    page.fixture.destroy();
    complete(new Blob(['private']));
    await Promise.resolve();
    await Promise.resolve();
    expect(page.download).not.toHaveBeenCalled();
  });

  it('rejects cross-account, malformed and non-full-amount list records', () => {
    expect(confirmations([row], account, order)).toEqual([row]);
    expect(() => confirmations(Array.from({ length: 26 }, (_, i) => ({ ...row,
      confirmationId: `00000000-0000-4000-8000-${String(i + 10).padStart(12, '0')}` })), account, order)).toThrow();
    for (const patch of [{ accountId: id }, { orderId: id }, { amountPaise: 0 }, { amountPaise: 1.2 },
      { issuanceBasis: 'Invented' }, { verifiedAt: 'bad' }, { confirmationId: '../file' }])
      expect(() => confirmations([{ ...row, ...patch }], account, order)).toThrow();
  });
});
