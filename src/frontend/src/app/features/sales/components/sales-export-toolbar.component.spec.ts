import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { SalesExportService } from '../services/sales-export.service';
import { SalesExportToolbarComponent } from './sales-export-toolbar.component';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<string, unknown>;

const salesExportService = {
  exportSales: vi.fn(),
  extractFilename: vi.fn(),
  triggerDownload: vi.fn(),
};

describe('SalesExportToolbarComponent', () => {
  beforeEach(() => {
    salesExportService.exportSales.mockReset();
    salesExportService.extractFilename.mockReset();
    salesExportService.triggerDownload.mockReset();

    salesExportService.extractFilename.mockReturnValue('sales-export.xlsx');
    salesExportService.triggerDownload.mockReturnValue(undefined);

    TestBed.configureTestingModule({
      imports: [
        SalesExportToolbarComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
      providers: [{ provide: SalesExportService, useValue: salesExportService }],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('defaults date range to the last 30 days and level to summary', async () => {
    const fixture = TestBed.createComponent(SalesExportToolbarComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    const today = new Date();
    const expectedStart = new Date(today);
    expectedStart.setDate(today.getDate() - 30);

    expect(component.startDate().toDateString()).toBe(expectedStart.toDateString());
    expect(component.endDate().toDateString()).toBe(today.toDateString());
    expect(component.level()).toBe('summary');
  });

  it('accepts controlled date and level inputs', async () => {
    const startDate = new Date('2026-05-01T12:00:00.000Z');
    const endDate = new Date('2026-05-13T12:00:00.000Z');

    const fixture = TestBed.createComponent(SalesExportToolbarComponent);
    fixture.componentRef.setInput('startDate', startDate);
    fixture.componentRef.setInput('endDate', endDate);
    fixture.componentRef.setInput('level', 'lineItems');
    fixture.detectChanges();

    expect(fixture.componentInstance.startDate().toISOString()).toBe(startDate.toISOString());
    expect(fixture.componentInstance.endDate().toISOString()).toBe(endDate.toISOString());
    expect(fixture.componentInstance.level()).toBe('lineItems');
  });

  it('exports the controlled values with requested format', () => {
    const response = new HttpResponse({ status: 200, body: new Blob(['sales']) });
    salesExportService.exportSales.mockReturnValue(of(response));

    const fixture = TestBed.createComponent(SalesExportToolbarComponent);
    const startDate = new Date('2026-05-01T12:00:00.000Z');
    const endDate = new Date('2026-05-13T12:00:00.000Z');
    fixture.componentRef.setInput('startDate', startDate);
    fixture.componentRef.setInput('endDate', endDate);
    fixture.componentRef.setInput('level', 'lineItems');
    fixture.detectChanges();

    fixture.componentInstance.exportToPdf();

    expect(salesExportService.exportSales).toHaveBeenCalledWith({
      format: 'pdf',
      level: 'lineItems',
      startDate: formatLocalIsoDate(startDate),
      endDate: formatLocalIsoDate(endDate),
    });
    expect(salesExportService.triggerDownload).toHaveBeenCalledWith(response.body!, 'sales-export.xlsx');
  });

  it('rejects invalid date ranges before exporting', () => {
    const fixture = TestBed.createComponent(SalesExportToolbarComponent);
    const transloco = TestBed.inject(TranslocoService);
    fixture.componentRef.setInput('startDate', new Date('2026-05-13T12:00:00.000Z'));
    fixture.componentRef.setInput('endDate', new Date('2026-05-01T12:00:00.000Z'));
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('button[data-export-format="xlsx"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(salesExportService.exportSales).not.toHaveBeenCalled();
    expect(fixture.componentInstance.exportError()).toBe(transloco.translate('sales.export.validation.invalidDateRange'));
  });

  it('disables export controls while export is in progress', () => {
    const subject = new Subject<HttpResponse<Blob>>();
    salesExportService.exportSales.mockReturnValue(subject.asObservable());

    const fixture = TestBed.createComponent(SalesExportToolbarComponent);
    fixture.detectChanges();
    const native = fixture.nativeElement as HTMLElement;
    const button = native.querySelector('button[data-export-format="xlsx"]') as HTMLButtonElement;

    button.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.isExporting()).toBe(true);
    expect(button.disabled).toBe(true);
    expect(native.querySelector('button[data-export-format="pdf"]')).not.toBeNull();

    const response = new HttpResponse({ status: 200, body: new Blob(['sales']) });
    subject.next(response);
    subject.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.isExporting()).toBe(false);
    expect(button.disabled).toBe(false);
  });

  it('shows backend errors as export error messages', async () => {
    const responseError = new HttpErrorResponse({
      status: 500,
      error: { message: 'Export generation failed' },
    });
    salesExportService.exportSales.mockReturnValue(throwError(() => responseError));

    const fixture = TestBed.createComponent(SalesExportToolbarComponent);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button[data-export-format="tallyXml"]') as HTMLButtonElement;

    button.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.exportError()).toBe('Export generation failed');

    const errorText = fixture.nativeElement.querySelector('[data-export-error]')?.textContent;
    expect(errorText).toContain('Export generation failed');
  });
});
