import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import {
  BILLING_DOCUMENT_FILENAME_PREFIX,
  BILLING_DOCUMENT_FILENAME_SUFFIX,
  BILLING_DOCUMENT_KIND,
  BILLING_DOCUMENT_LIST_BEFORE_TEMPLATE,
  BILLING_DOCUMENT_MEDIA_TYPE,
  BILLING_DOCUMENT_PAGE_SIZE,
  BILLING_DOCUMENT_SCHEMA_VERSION,
  EARLIEST_ISSUE_DATE,
  MAX_BILLING_DOCUMENT_BYTES,
  MAX_ISSUER_NAME_LENGTH,
  MAX_ISSUER_REFERENCE_LENGTH,
  billingDocumentFileName,
  parseBillingDocumentList,
  type BillingDocument,
} from './billing-document.models';
import { BillingDocumentService } from './billing-document.service';

const account = '00000000-0000-4000-8000-000000000001';
const order = '00000000-0000-4000-8000-000000000002';
const docId = '00000000-0000-4000-8000-000000000003';
const versionId = '00000000-0000-4000-8000-000000000004';
const scope = { userId: 'payer', shopId: 'shop', account };

async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  const digest = await crypto.subtle.digest('SHA-256', copy.buffer);
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('');
}

const pdfBytes = new TextEncoder().encode('%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n');

function sampleRow(hash: string, byteLength = pdfBytes.byteLength): BillingDocument {
  return {
    accountId: account,
    orderId: order,
    documentId: docId,
    versionId,
    version: 1,
    supersedesVersionId: null,
    kind: BILLING_DOCUMENT_KIND,
    mediaType: BILLING_DOCUMENT_MEDIA_TYPE,
    byteLength,
    sha256: hash,
    issuerName: 'Fixture Accounting',
    issuerReference: 'FIXTURE-INV-114',
    issuedOn: '2027-03-10',
    uploadedAt: '2027-03-10T18:29:59Z',
  };
}

describe('BillingDocumentService and contract', () => {
  let service: BillingDocumentService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BillingDocumentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('pins constants and sample shape against tests/contracts/licensing/billing-document-v1.json', () => {
    const fixturePath = resolve(process.cwd(), '../../tests/contracts/licensing/billing-document-v1.json');
    const contract = JSON.parse(readFileSync(fixturePath, 'utf8'));
    expect(BILLING_DOCUMENT_SCHEMA_VERSION).toBe(contract.schemaVersion);
    expect(BILLING_DOCUMENT_KIND).toBe(contract.kind);
    expect(BILLING_DOCUMENT_MEDIA_TYPE).toBe(contract.mediaType);
    expect(MAX_BILLING_DOCUMENT_BYTES).toBe(contract.maxDocumentBytes);
    expect(BILLING_DOCUMENT_PAGE_SIZE).toBe(contract.pageSize);
    expect(MAX_ISSUER_NAME_LENGTH).toBe(contract.maxIssuerNameLength);
    expect(MAX_ISSUER_REFERENCE_LENGTH).toBe(contract.maxIssuerReferenceLength);
    expect(EARLIEST_ISSUE_DATE).toBe(contract.earliestIssueDate);
    expect(BILLING_DOCUMENT_FILENAME_PREFIX).toBe(contract.fileNamePrefix);
    expect(BILLING_DOCUMENT_FILENAME_SUFFIX).toBe(contract.fileNameSuffix);
    expect(BILLING_DOCUMENT_LIST_BEFORE_TEMPLATE).toBe(contract.listBeforePathTemplate);
    expect(billingDocumentFileName(versionId)).toBe(`accounting-invoice-${versionId}.pdf`);
    expect(parseBillingDocumentList(contract.sample, contract.sample.accountId, contract.sample.orderId)).toHaveLength(1);
  });

  it('requests bounded older pages via /before/{beforeVersion} and rejects invalid cursors or order', async () => {
    const v1 = sampleRow('a'.repeat(64));
    const v2: BillingDocument = {
      ...v1,
      versionId: '00000000-0000-4000-8000-000000000005',
      version: 2,
      supersedesVersionId: versionId,
      issuerReference: 'FIXTURE-INV-114-CORR-1',
    };

    const pending = service.list(scope, order, 2);
    http
      .expectOne(`${API_BASE_URL}/billing/accounts/${account}/orders/${order}/billing-documents/before/2`)
      .flush({ schemaVersion: 1, accountId: account, orderId: order, documents: [v1] });
    expect(await pending).toEqual([v1]);

    await expect(service.list(scope, order, 1)).rejects.toThrow('Invalid billing document cursor');
    await expect(service.list(scope, order, 0)).rejects.toThrow('Invalid billing document cursor');
    expect(() =>
      parseBillingDocumentList({ schemaVersion: 1, accountId: account, orderId: order, documents: [v1, v2] }, account, order),
    ).toThrow();
    expect(() =>
      parseBillingDocumentList({ schemaVersion: 1, accountId: account, orderId: order, documents: [v2] }, account, order, 2),
    ).toThrow();
  });

  it('validates listed metadata and rejects cross-account, bad hash, oversize and duplicate rows', () => {
    const valid = sampleRow('a'.repeat(64));
    const list = { schemaVersion: 1, accountId: account, orderId: order, documents: [valid] };
    expect(parseBillingDocumentList(list, account, order)).toEqual([valid]);

    for (const patch of [
      { accountId: '00000000-0000-4000-8000-000000000099' },
      { orderId: '00000000-0000-4000-8000-000000000099' },
      { kind: 'PaymentConfirmation' },
      { mediaType: 'text/html' },
      { byteLength: 0 },
      { byteLength: MAX_BILLING_DOCUMENT_BYTES + 1 },
      { sha256: 'not-a-hash' },
      { sha256: 'A'.repeat(64) },
      { issuerName: '  padded  ' },
      { issuerReference: 'BAD\nREF' },
      { issuedOn: '1999-12-31' },
      { versionId: '../escape' },
    ]) {
      expect(() =>
        parseBillingDocumentList({ ...list, documents: [{ ...valid, ...patch }] }, account, order),
      ).toThrow();
    }

    expect(() =>
      parseBillingDocumentList({ ...list, documents: [valid, valid] }, account, order),
    ).toThrow();
  });

  it('returns an octet-stream download blob only when type, length, PDF prefix and SHA-256 match', async () => {
    const hash = await sha256Hex(pdfBytes);
    const row = sampleRow(hash);
    const pending = service.content(scope, order, row);
    const req = http.expectOne(
      `${API_BASE_URL}/billing/accounts/${account}/orders/${order}/billing-documents/${versionId}/content`,
    );
    req.flush(new Blob([pdfBytes], { type: 'application/pdf' }));
    const saved = await pending;
    expect(saved.type).toBe('application/octet-stream');
    expect(Array.from(new Uint8Array(await saved.arrayBuffer()))).toEqual(Array.from(pdfBytes));
  });

  it('refuses wrong media type, length mismatch, missing PDF header and SHA-256 mismatch', async () => {
    const hash = await sha256Hex(pdfBytes);
    const url = `${API_BASE_URL}/billing/accounts/${account}/orders/${order}/billing-documents/${versionId}/content`;

    const wrongType = service.content(scope, order, sampleRow(hash));
    http.expectOne(url).flush(new Blob([pdfBytes], { type: 'text/html' }));
    await expect(wrongType).rejects.toThrow('Invalid billing document media type');

    const wrongLength = service.content(scope, order, sampleRow(hash, pdfBytes.byteLength + 1));
    http.expectOne(url).flush(new Blob([pdfBytes], { type: 'application/pdf' }));
    await expect(wrongLength).rejects.toThrow('Invalid billing document content');

    const nonPdfBytes = new TextEncoder().encode('<html>not a pdf</html>');
    const nonPdfHash = await sha256Hex(nonPdfBytes);
    const missingPdfPrefix = service.content(scope, order, sampleRow(nonPdfHash, nonPdfBytes.byteLength));
    http.expectOne(url).flush(new Blob([nonPdfBytes], { type: 'application/pdf' }));
    await expect(missingPdfPrefix).rejects.toThrow('Invalid billing document content');

    const wrongHash = service.content(scope, order, sampleRow('f'.repeat(64)));
    http.expectOne(url).flush(new Blob([pdfBytes], { type: 'application/pdf' }));
    await expect(wrongHash).rejects.toThrow('Billing document hash mismatch');
  });
});
