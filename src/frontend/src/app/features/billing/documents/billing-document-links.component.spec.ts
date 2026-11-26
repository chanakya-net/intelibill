import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { BlobDownloadService } from '../../../shared/utils/blob-download.service';
import { BillingDocumentLinksComponent } from './billing-document-links.component';
import type { BillingDocument } from './billing-document.models';
import { BillingDocumentService } from './billing-document.service';

const account = '00000000-0000-4000-8000-000000000001';
const order = '00000000-0000-4000-8000-000000000002';
const docId = '00000000-0000-4000-8000-000000000003';
const versionId = '00000000-0000-4000-8000-000000000004';
const hash = 'b'.repeat(64);
const row: BillingDocument = {
  accountId: account,
  orderId: order,
  documentId: docId,
  versionId,
  version: 1,
  supersedesVersionId: null,
  kind: 'AccountingInvoice',
  mediaType: 'application/pdf',
  byteLength: 512,
  sha256: hash,
  issuerName: 'Fixture Accounting',
  issuerReference: 'FIXTURE-INV-114',
  issuedOn: '2027-03-10',
  uploadedAt: '2027-03-10T18:29:59Z',
};

afterEach(() => TestBed.resetTestingModule());

function setup(
  content: () => Promise<Blob>,
  list: () => Promise<readonly BillingDocument[]> = async () => [row],
) {
  const scope = signal<{ userId: string; shopId: string } | null>({ userId: 'payer', shopId: 'shop' });
  const download = vi.fn();
  const openPdf = vi.fn();
  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { 'en-IN': en },
        translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
        preloadLangs: true,
      }),
    ],
    providers: [
      { provide: ShopContextService, useValue: { scope } },
      { provide: BillingDocumentService, useValue: { list, content } },
      { provide: BlobDownloadService, useValue: { download, openPdf } },
    ],
  });
  const fixture = TestBed.createComponent(BillingDocumentLinksComponent);
  fixture.componentRef.setInput('account', account);
  fixture.componentRef.setInput('order', order);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const click = (testId: string) => root.querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`)!.click();
  const text = (testId: string) => root.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null;
  const settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  return { fixture, root, click, text, settle, scope, download, openPdf };
}

describe('BillingDocumentLinksComponent', () => {
  it('lists issuer metadata and downloads verified bytes without inline PDF rendering', async () => {
    const blob = new Blob(['%PDF-1.7'], { type: 'application/octet-stream' });
    const page = setup(async () => blob);

    expect(page.text('billing-documents-heading')).toBe(en.billing.documents.heading);
    expect(page.text('billing-documents-heading')).not.toBe(en.billing.checkout.payment.confirmation.view);
    expect(page.text('billing-documents-purpose')).toContain('separate from your automatic payment confirmation');

    page.click('billing-documents-list');
    await page.settle();

    expect(page.text('billing-document-issuer')).toBe('Fixture Accounting');
    expect(page.text('billing-document-reference')).toBe('FIXTURE-INV-114');
    expect(page.text('billing-document-issued-on')).toBe('2027-03-10');
    expect(page.text('billing-document-version')).toBe('1');
    expect(page.text('billing-document-hash')).toBe(hash);

    page.click('billing-document-download');
    await page.settle();

    expect(page.download).toHaveBeenCalledExactlyOnceWith(blob, `accounting-invoice-${versionId}.pdf`);
    expect(page.openPdf).not.toHaveBeenCalled();
    expect(page.root.querySelectorAll('iframe, embed, object, a[href^="blob:"]')).toHaveLength(0);
  });

  it('shows empty state when no accounting invoice is attached yet', async () => {
    const page = setup(async () => new Blob([]), async () => []);
    page.click('billing-documents-list');
    await page.settle();
    expect(page.text('billing-documents-empty')).toBe(en.billing.documents.empty);
    expect(page.root.querySelector('[data-testid="billing-document-download"]')).toBeNull();
  });

  it('drops a download that finishes after scope changes', async () => {
    let finish!: (blob: Blob) => void;
    const page = setup(() => new Promise((resolve) => { finish = resolve; }));
    page.click('billing-documents-list');
    await page.settle();
    page.click('billing-document-download');
    page.scope.set({ userId: 'other', shopId: 'other-shop' });
    page.fixture.detectChanges();
    finish(new Blob(['%PDF-1.7'], { type: 'application/octet-stream' }));
    await page.settle();
    expect(page.download).not.toHaveBeenCalled();
    expect(page.root.querySelector('[data-testid="billing-document-download"]')).toBeNull();
  });

  it('drops a download after the component is destroyed', async () => {
    let finish!: (blob: Blob) => void;
    const page = setup(() => new Promise((resolve) => { finish = resolve; }));
    page.click('billing-documents-list');
    await page.settle();
    page.click('billing-document-download');
    page.fixture.destroy();
    finish(new Blob(['%PDF-1.7'], { type: 'application/octet-stream' }));
    await Promise.resolve();
    await Promise.resolve();
    expect(page.download).not.toHaveBeenCalled();
  });
});
