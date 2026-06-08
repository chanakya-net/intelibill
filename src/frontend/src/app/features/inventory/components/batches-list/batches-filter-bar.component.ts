import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { inject } from '@angular/core';

import type { BatchFilters } from '../../services/inventory.models';
import { formatLocalIsoDate, parseDateOnlyAsLocalDate } from '../../../../shared/utils/date-time.util';

@Component({
  selector: 'app-batches-filter-bar',
  standalone: true,
  imports: [
    FormsModule,
    ButtonModule,
    DatePickerModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './batches-filter-bar.component.html',
  styleUrl: './batches-filter-bar.component.scss',
})
export class BatchesFilterBarComponent {
  private readonly translocoService = inject(TranslocoService);

  private _filters: BatchFilters = { search: '', status: 'all', fromDate: '', toDate: '' };

  @Input({ required: true })
  set filters(value: BatchFilters) {
    this._filters = value;
    this.fromDateValue = this.parseFilterDate(value.fromDate);
    this.toDateValue = this.parseFilterDate(value.toDate);
  }
  get filters(): BatchFilters {
    return this._filters;
  }

  @Output() filtersChange = new EventEmitter<BatchFilters>();

  protected fromDateValue: Date | null = null;
  protected toDateValue: Date | null = null;

  protected readonly statusOptions: Array<{ label: string; value: 'all' | 'active' | 'voided' }> = [
    { label: this.translocoService.translate('common.all'), value: 'all' },
    { label: this.translocoService.translate('inventory.active'), value: 'active' },
    { label: this.translocoService.translate('inventory.voided'), value: 'voided' },
  ];

  onSearchChange(value: string): void {
    this.emitFilters({ ...this.filters, search: value ?? '' });
  }

  onStatusChange(status: 'all' | 'active' | 'voided'): void {
    this.emitFilters({ ...this.filters, status });
  }

  onDateFromChange(value: Date | null): void {
    this.emitFilters({ ...this.filters, fromDate: value ? formatLocalIsoDate(value) : '' });
  }

  onDateToChange(value: Date | null): void {
    this.emitFilters({ ...this.filters, toDate: value ? formatLocalIsoDate(value) : '' });
  }

  private parseFilterDate(value: string): Date | null {
    const normalized = value?.trim();
    if (!normalized) {
      return null;
    }

    const parsed = parseDateOnlyAsLocalDate(normalized);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  }

  onClearFilters(): void {
    this.emitFilters({
      ...this.filters,
      search: '',
      status: 'all',
      fromDate: '',
      toDate: '',
    });
  }

  protected emitFilters(filters: BatchFilters): void {
    this.filtersChange.emit(filters);
  }
}
