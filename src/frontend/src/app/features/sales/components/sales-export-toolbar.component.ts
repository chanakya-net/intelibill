import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';

import { SalesExportFormat, SalesExportParams, SalesExportService } from '../services/sales-export.service';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

@Component({
  selector: 'app-sales-export-toolbar',
  standalone: true,
  imports: [CommonModule, ButtonModule, TranslocoPipe],
  templateUrl: './sales-export-toolbar.component.html',
  styleUrl: './sales-export-toolbar.component.scss',
})
export class SalesExportToolbarComponent {
  private readonly exportService = inject(SalesExportService);
  private readonly translocoService = inject(TranslocoService);

  readonly startDate = input<Date>(this.getDefaultStartDate());
  readonly endDate = input<Date>(this.getDefaultEndDate());
  readonly level = input<SalesExportParams['level']>('summary');
  readonly isExporting = signal(false);
  readonly exportError = signal('');

  exportToExcel(): void {
    this.export('xlsx');
  }

  exportToPdf(): void {
    this.export('pdf');
  }

  exportToTally(): void {
    this.export('tallyXml');
  }

  private export(format: SalesExportFormat): void {
    if (!this.validateDateRange()) {
      return;
    }

    this.exportError.set('');
    this.isExporting.set(true);

    const params: SalesExportParams = {
      format,
      level: this.level(),
      startDate: this.formatIsoDate(this.startDate()),
      endDate: this.formatIsoDate(this.endDate()),
    };

    this.exportService.exportSales(params).subscribe({
      next: (response) => {
        this.isExporting.set(false);
        this.saveExportResponse(response);
      },
      error: (error: unknown) => {
        this.isExporting.set(false);
        this.exportError.set(this.extractExportError(error));
      },
    });
  }

  private saveExportResponse(response: HttpResponse<Blob>): void {
    const filename = this.exportService.extractFilename(response);
    const blob = response.body;

    if (!blob) {
      this.exportError.set(this.translocoService.translate('sales.export.error.unexpected'));
      return;
    }

    this.exportService.triggerDownload(blob, filename);
  }

  private validateDateRange(): boolean {
    const start = this.startDate();
    const end = this.endDate();

    if (!start || !end) {
      this.exportError.set(this.translocoService.translate('sales.export.validation.dateRequired'));
      return false;
    }

    if (start > end) {
      this.exportError.set(this.translocoService.translate('sales.export.validation.invalidDateRange'));
      return false;
    }

    return true;
  }

  private extractExportError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const responseError = error.error;

      if (typeof responseError === 'string' && responseError.trim().length > 0) {
        return responseError;
      }

      if (
        responseError !== null &&
        typeof responseError === 'object' &&
        'message' in responseError &&
        typeof (responseError as { message?: unknown }).message === 'string'
      ) {
        return (responseError as { message: string }).message;
      }

      if (error.message) {
        return error.message;
      }
    }

    return this.translocoService.translate('sales.export.error.unexpected');
  }

  private getDefaultStartDate(referenceDate = new Date()): Date {
    const date = new Date(referenceDate);
    date.setDate(date.getDate() - 30);
    return date;
  }

  private getDefaultEndDate(referenceDate = new Date()): Date {
    return new Date(referenceDate);
  }

  private formatIsoDate(date: Date): string {
    return formatLocalIsoDate(date);
  }
}
