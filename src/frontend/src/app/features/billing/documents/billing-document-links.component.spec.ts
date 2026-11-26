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
  content: (scope: unknown, orderId: string, doc: BillingDocument) => Promise<Blob>,
  list: (scope: unknown, orderId: string, beforeVersion?: number) => Promise<readonly BillingDocument[]> = async () => [row],
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

  it('labels only the head Current, earlier rows Historical, paginates older versions and ignores stale pages', async () => {
    const v2Id = '00000000-0000-4000-8000-000000000005';
    const v3Id = '00000000-0000-4000-8000-000000000006';
    const v2: BillingDocument = { ...row, versionId: v2Id, version: 2, supersedesVersionId: versionId, issuerReference: 'INV-V2' };
    const v3: BillingDocument = { ...row, versionId: v3Id, version: 3, supersedesVersionId: v2Id, issuerReference: 'INV-V3' };

    const calls: (number | undefined)[] = [];
    const list = vi.fn(async (_scope: unknown, _order: string, beforeVersion?: number) => {
      calls.push(beforeVersion);
      return beforeVersion === undefined ? [v3, v2] : [row];
    });
    const page = setup(async () => new Blob(['%PDF-1.7'], { type: 'application/octet-stream' }), list);

    page.click('billing-documents-list');
    await page.settle();

    const statuses = () => [...page.root.querySelectorAll('[data-testid="billing-document-status"]')].map((n) => n.textContent?.trim());
    const versions = () => [...page.root.querySelectorAll('[data-testid="billing-document-version"]')].map((n) => n.textContent?.trim());
    expect(versions()).toEqual(['3', '2']);
    expect(statuses()).toEqual([en.billing.documents.current, en.billing.documents.historical]);
    expect(page.root.querySelector('[data-testid="billing-documents-older"]')).not.toBeNull();

    page.click('billing-documents-older');
    await page.settle();

    expect(calls).toEqual([undefined, 2]);
    expect(versions()).toEqual(['3', '2', '1']);
    expect(statuses()).toEqual([
      en.billing.documents.current,
      en.billing.documents.historical,
      en.billing.documents.historical,
    ]);
    expect(page.root.querySelector('[data-testid="billing-documents-older"]')).toBeNull();
  });

  it('drops an older-page response that finishes after scope changes', async () => {
    const v2Id = '00000000-0000-4000-8000-000000000005';
    const v3Id = '00000000-0000-4000-8000-000000000006';
    const v2: BillingDocument = { ...row, versionId: v2Id, version: 2, supersedesVersionId: versionId, issuerReference: 'INV-V2' };
    const v3: BillingDocument = { ...row, versionId: v3Id, version: 3, supersedesVersionId: v2Id, issuerReference: 'INV-V3' };

    let resolveOlder!: (docs: readonly BillingDocument[]) => void;
    const delayed = setup(
      async () => new Blob(['%PDF-1.7'], { type: 'application/octet-stream' }),
      (_scope, _order, beforeVersion) =>
        beforeVersion === undefined ? Promise.resolve([v3, v2]) : new Promise((resolve) => { resolveOlder = resolve; }),
    );
    delayed.click('billing-documents-list');
    await delayed.settle();
    delayed.click('billing-documents-older');
    delayed.scope.set({ userId: 'other-payer', shopId: 'other-shop' });
    delayed.fixture.detectChanges();
    resolveOlder([row]);
    await delayed.settle();
    expect(delayed.root.querySelectorAll('[data-testid="billing-document-item"]')).toHaveLength(0);
  });
});
