export const MAX_PDF_BYTES = 10 * 1024 * 1024;
export const PDF_MEDIA_TYPE = 'application/pdf';
export const ACCOUNTING_INVOICE = 'AccountingInvoice';
const fileName = /^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,114}\.[Pp][Dd][Ff](?![\s\S])/;
const control = /[\u0000-\u001f\u007f-\u009f]/;

export interface InvoiceMetadata {
  readonly kind: typeof ACCOUNTING_INVOICE;
  readonly fileName: string;
  readonly mediaType: typeof PDF_MEDIA_TYPE;
  readonly byteLength: number;
  readonly sha256: string;
  readonly issuerName: string;
  readonly issuerReference: string;
  readonly issuedOn: string;
}

export interface InvoiceIntent {
  readonly operationKey: string;
  readonly metadata: InvoiceMetadata;
  readonly previewDigest: string;
  readonly bytes: ArrayBuffer;
}

export async function prepareInvoice(file: File | null, form: HTMLFormElement) {
  if (!file || file.size === 0 || !fileName.test(file.name) || (file.type && file.type !== PDF_MEDIA_TYPE))
    throw new InvoiceInputError('Select a PDF with a valid file name.');
  if (file.size > MAX_PDF_BYTES) throw new InvoiceInputError('PDFs must be no larger than 10 MiB.');

  const values = new FormData(form);
  const issuerName = issuerField(values.get('issuerName'), 200);
  const issuerReference = issuerField(values.get('issuerReference'), 64);
  const issuedOn = values.get('issuedOn');
  if (!issuerName || !issuerReference || typeof issuedOn !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(issuedOn))
    throw new InvoiceInputError('Enter the issuer, issuing reference and issue date.');

  const bytes = await file.arrayBuffer();
  const header = new Uint8Array(bytes, 0, Math.min(9, bytes.byteLength));
  if (!/^%PDF-(1\.[0-7]|2\.0)[\r\n]$/.test(String.fromCharCode(...header)))
    throw new InvoiceInputError('Select a valid PDF.');
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  const sha256 = Array.from(hash, value => value.toString(16).padStart(2, '0')).join('');
  const metadata: InvoiceMetadata = {
    kind: ACCOUNTING_INVOICE, fileName: file.name, mediaType: PDF_MEDIA_TYPE,
    byteLength: bytes.byteLength, sha256, issuerName, issuerReference, issuedOn,
  };
  return { bytes, metadata };
}

function issuerField(value: FormDataEntryValue | null, max: number): string {
  const text = typeof value === 'string' ? value.trim() : '';
  return text.length <= max && !control.test(text) ? text : '';
}

export class InvoiceInputError extends Error {}
