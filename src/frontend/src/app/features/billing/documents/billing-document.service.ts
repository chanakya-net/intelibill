import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import type { CheckoutScope } from '../checkout/checkout.models';
import {
  BILLING_DOCUMENT_MEDIA_TYPE,
  MAX_BILLING_DOCUMENT_BYTES,
  parseBillingDocumentList,
  type BillingDocument,
} from './billing-document.models';

const PDF_PREFIX = [0x25, 0x50, 0x44, 0x46, 0x2d] as const;

function hasPdfPrefix(bytes: Uint8Array): boolean {
  return bytes.byteLength >= PDF_PREFIX.length && PDF_PREFIX.every((byte, index) => bytes[index] === byte);
}

async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  const digest = await crypto.subtle.digest('SHA-256', copy.buffer);
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('');
}

@Injectable({ providedIn: 'root' })
export class BillingDocumentService {
  private readonly http = inject(HttpClient);

  async list(scope: CheckoutScope, order: string, beforeVersion?: number): Promise<readonly BillingDocument[]> {
    if (beforeVersion !== undefined && (!Number.isSafeInteger(beforeVersion) || beforeVersion <= 1)) {
      throw new Error('Invalid billing document cursor');
    }
    const url =
      beforeVersion === undefined
        ? this.path(scope, order)
        : `${this.path(scope, order)}/before/${beforeVersion}`;
    const value = await firstValueFrom(
      this.http.get<unknown>(url, { context: scopedRequestContext(scope) }),
    );
    return parseBillingDocumentList(value, scope.account, order, beforeVersion);
  }

  async content(scope: CheckoutScope, order: string, doc: BillingDocument): Promise<Blob> {
    const response = await firstValueFrom(
      this.http.get(`${this.path(scope, order)}/${encodeURIComponent(doc.versionId)}/content`, {
        context: scopedRequestContext(scope),
        responseType: 'blob',
        observe: 'response',
      }),
    );
    const body = response.body;
    const mediaType = body?.type.split(';')[0].trim().toLowerCase();
    if (!body || mediaType !== BILLING_DOCUMENT_MEDIA_TYPE) {
      throw new Error('Invalid billing document media type');
    }
    const bytes = new Uint8Array(await body.arrayBuffer());
    if (
      bytes.byteLength === 0 ||
      bytes.byteLength > MAX_BILLING_DOCUMENT_BYTES ||
      bytes.byteLength !== doc.byteLength ||
      !hasPdfPrefix(bytes)
    ) {
      throw new Error('Invalid billing document content');
    }
    if ((await sha256Hex(bytes)) !== doc.sha256) {
      throw new Error('Billing document hash mismatch');
    }
    // Re-wrap as octet-stream so downloads save directly and never trigger inline PDF execution.
    return new Blob([bytes], { type: 'application/octet-stream' });
  }

  private path(scope: CheckoutScope, order: string): string {
    return `${API_BASE_URL}/billing/accounts/${encodeURIComponent(scope.account)}/orders/${encodeURIComponent(order)}/billing-documents`;
  }
}
