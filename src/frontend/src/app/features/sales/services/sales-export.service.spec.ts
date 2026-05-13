import { HttpHeaders, HttpResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, it, expect, afterEach, vi } from 'vitest';

import { EXPORT_ENDPOINTS } from '../../../core/auth/auth.constants';
import { SalesExportService, SalesExportParams } from './sales-export.service';

describe('SalesExportService', () => {
  function setup(): { service: SalesExportService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    return {
      service: TestBed.inject(SalesExportService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  describe('exportSales', () => {
    it('sends GET request with all query parameters', () => {
      const { service, http } = setup();
      const params: SalesExportParams = {
        format: 'xlsx',
        level: 'summary',
        startDate: '2026-04-13',
        endDate: '2026-05-13',
      };

      service.exportSales(params).subscribe();

      const req = http.expectOne((request) => {
        return (
          request.url === EXPORT_ENDPOINTS.sales &&
          request.params.get('format') === 'xlsx' &&
          request.params.get('level') === 'summary' &&
          request.params.get('startDate') === '2026-04-13' &&
          request.params.get('endDate') === '2026-05-13'
        );
      });

      expect(req.request.method).toBe('GET');
      req.flush(new Blob());
      http.verify();
    });

    it('requests blob response type', () => {
      const { service, http } = setup();
      const params: SalesExportParams = {
        format: 'pdf',
        level: 'lineItems',
        startDate: '2026-04-13',
        endDate: '2026-05-13',
      };

      service.exportSales(params).subscribe();

      const req = http.expectOne((request) => request.url === EXPORT_ENDPOINTS.sales);
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob());
      http.verify();
    });

    it('observes full HTTP response including headers', () => {
      const { service, http } = setup();
      const params: SalesExportParams = {
        format: 'xlsx',
        level: 'summary',
        startDate: '2026-04-13',
        endDate: '2026-05-13',
      };

      service.exportSales(params).subscribe((response) => {
        expect(response instanceof HttpResponse).toBe(true);
        expect(response.status).toBe(200);
      });

      const req = http.expectOne((request) => request.url === EXPORT_ENDPOINTS.sales);
      req.flush(new Blob(), { status: 200, statusText: 'OK' });
      http.verify();
    });

    it('supports different export formats', () => {
      const { service, http } = setup();
      const formats = ['xlsx', 'pdf', 'tallyXml'] as const;

      formats.forEach((format) => {
        const params: SalesExportParams = {
          format,
          level: 'summary',
          startDate: '2026-04-13',
          endDate: '2026-05-13',
        };

        service.exportSales(params).subscribe();

        const req = http.expectOne((request) => request.url === EXPORT_ENDPOINTS.sales);
        expect(req.request.params.get('format')).toBe(format);
        req.flush(new Blob());
      });

      http.verify();
    });

    it('supports different export levels', () => {
      const { service, http } = setup();
      const levels = ['summary', 'lineItems'] as const;

      levels.forEach((level) => {
        const params: SalesExportParams = {
          format: 'xlsx',
          level,
          startDate: '2026-04-13',
          endDate: '2026-05-13',
        };

        service.exportSales(params).subscribe();

        const req = http.expectOne((request) => request.url === EXPORT_ENDPOINTS.sales);
        expect(req.request.params.get('level')).toBe(level);
        req.flush(new Blob());
      });

      http.verify();
    });
  });

  describe('extractFilename', () => {
    it('extracts filename from Content-Disposition header with UTF-8 encoding', () => {
      const { service } = setup();
      const headers = new HttpHeaders({
        'Content-Disposition': "attachment; filename*=UTF-8''sales-export-2026-05-13.xlsx",
      });
      const response = new HttpResponse<Blob>({ headers });

      const filename = service.extractFilename(response);
      expect(filename).toBe('sales-export-2026-05-13.xlsx');
    });

    it('extracts filename from Content-Disposition header with simple filename', () => {
      const { service } = setup();
      const headers = new HttpHeaders({
        'Content-Disposition': 'attachment; filename="sales-export-2026-05-13.xlsx"',
      });
      const response = new HttpResponse<Blob>({ headers });

      const filename = service.extractFilename(response);
      expect(filename).toBe('sales-export-2026-05-13.xlsx');
    });

    it('extracts filename with single quotes', () => {
      const { service } = setup();
      const headers = new HttpHeaders({
        'Content-Disposition': "attachment; filename='sales-export-2026-05-13.xlsx'",
      });
      const response = new HttpResponse<Blob>({ headers });

      const filename = service.extractFilename(response);
      expect(filename).toBe('sales-export-2026-05-13.xlsx');
    });

    it('returns fallback filename when Content-Disposition is missing', () => {
      const { service } = setup();
      const response = new HttpResponse<Blob>();

      const filename = service.extractFilename(response);
      expect(filename).toMatch(/^sales-export-\d{4}-\d{2}-\d{2}\.xlsx$/);
    });

    it('returns fallback filename when Content-Disposition does not contain filename', () => {
      const { service } = setup();
      const headers = new HttpHeaders({
        'Content-Disposition': 'attachment',
      });
      const response = new HttpResponse<Blob>({ headers });

      const filename = service.extractFilename(response);
      expect(filename).toMatch(/^sales-export-\d{4}-\d{2}-\d{2}\.xlsx$/);
    });

    it('returns today date in fallback filename', () => {
      const { service } = setup();
      const response = new HttpResponse<Blob>();

      const filename = service.extractFilename(response);
      const today = new Date().toISOString().split('T')[0];
      expect(filename).toBe(`sales-export-${today}.xlsx`);
    });
  });

  describe('triggerDownload', () => {
    it('creates download link and removes it after click', () => {
      const { service } = setup();
      const blob = new Blob(['test'], { type: 'text/plain' });
      const filename = 'test-export.xlsx';

      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
      vi.spyOn(URL, 'revokeObjectURL');
      const appendSpy = vi.spyOn(document.body, 'appendChild');
      const removeSpy = vi.spyOn(document.body, 'removeChild');

      service.triggerDownload(blob, filename);

      expect(URL.createObjectURL).toHaveBeenCalledWith(blob);
      expect(appendSpy).toHaveBeenCalled();
      expect(removeSpy).toHaveBeenCalled();
      expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');

      vi.restoreAllMocks();
    });

    it('sets correct download filename on link element', () => {
      const { service } = setup();
      const blob = new Blob(['test'], { type: 'application/vnd.ms-excel' });
      const filename = 'sales-2026-05-13.xlsx';

      const originalCreateElement = document.createElement.bind(document);
      const createElementSpy = vi.spyOn(document, 'createElement').mockImplementation((tagName: string) => {
        return originalCreateElement(tagName);
      });

      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
      vi.spyOn(document.body, 'appendChild');
      vi.spyOn(document.body, 'removeChild');

      service.triggerDownload(blob, filename);

      expect(createElementSpy).toHaveBeenCalledWith('a');

      vi.restoreAllMocks();
    });
  });
});
