import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { EXPORT_ENDPOINTS } from '../../../core/auth/auth.constants';

export type SalesExportFormat = 'xlsx' | 'pdf' | 'tallyXml';
export type SalesExportLevel = 'summary' | 'lineItems';

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

  extractFilename(response: HttpResponse<Blob>): string {
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

    return this.generateDefaultFilename();
  }

  private generateDefaultFilename(): string {
    const timestamp = new Date().toISOString().split('T')[0];
    return `sales-export-${timestamp}.xlsx`;
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
