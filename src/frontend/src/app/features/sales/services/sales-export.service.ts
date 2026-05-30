import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { EXPORT_ENDPOINTS } from '../../../core/auth/auth.constants';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

export type SalesExportFormat = 'xlsx' | 'pdf' | 'tallyXml';
export type SalesExportLevel = 'summary' | 'lineItems';

export interface ProfitLossExportParams {
  readonly from: string;
  readonly to: string;
  readonly type?: 'all' | 'sale' | 'saleReturn' | 'inventoryAdjustment';
  readonly search?: string;
  readonly format?: 'xlsx';
}

export interface SalesExportParams {
  readonly format: SalesExportFormat;
  readonly level: SalesExportLevel;
  readonly startDate: string; // ISO date string (YYYY-MM-DD)
  readonly endDate: string; // ISO date string (YYYY-MM-DD)
}

@Injectable({ providedIn: 'root' })
export class SalesExportService {
  private readonly http = inject(HttpClient);

  exportSales(params: SalesExportParams): Observable<HttpResponse<Blob>> {
    let httpParams = new HttpParams()
      .set('format', params.format)
      .set('level', params.level)
      .set('startDate', params.startDate)
      .set('endDate', params.endDate);

    return this.http.get(EXPORT_ENDPOINTS.sales, {
      params: httpParams,
      responseType: 'blob',
      observe: 'response',
    }) as Observable<HttpResponse<Blob>>;
  }

  exportProfitLoss(params: ProfitLossExportParams): Observable<HttpResponse<Blob>> {
    let httpParams = new HttpParams()
      .set('from', params.from)
      .set('to', params.to)
      .set('format', params.format ?? 'xlsx');

    if (params.type) {
      httpParams = httpParams.set('type', params.type);
    }

    if (params.search) {
      httpParams = httpParams.set('search', params.search);
    }

    return this.http.get(EXPORT_ENDPOINTS.profitLoss, {
      params: httpParams,
      responseType: 'blob',
      observe: 'response',
    }) as Observable<HttpResponse<Blob>>;
  }

  extractFilename(response: HttpResponse<Blob>): string {
    return this.extractFilenameWithPrefix(response, 'sales-export');
  }

  extractFilenameWithPrefix(response: HttpResponse<Blob>, fallbackPrefix: string): string {
    const contentDisposition = response.headers.get('Content-Disposition');

    if (contentDisposition) {
      const match = /filename\*=UTF-8''([^;]+)|filename=([^;]+)/.exec(contentDisposition);
      if (match) {
        const filename = match[1] ? decodeURIComponent(match[1]) : match[2]?.trim().replace(/^["']|["']$/g, '');
        if (filename) {
          return filename;
        }
      }
    }

    return this.generateDefaultFilename(fallbackPrefix);
  }

  private generateDefaultFilename(prefix: string): string {
    const timestamp = formatLocalIsoDate(new Date());
    return `${prefix}-${timestamp}.xlsx`;
  }

  triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }
}
