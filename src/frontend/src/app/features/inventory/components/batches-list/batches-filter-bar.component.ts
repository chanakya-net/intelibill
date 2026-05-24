import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

import type { BatchFilters } from '../../services/inventory.models';

@Component({
  selector: 'app-batches-filter-bar',
  standalone: true,
  imports: [
    FormsModule,
    ButtonModule,
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
  @Input({ required: true }) filters!: BatchFilters;
  @Output() filtersChange = new EventEmitter<BatchFilters>();

  protected readonly statusOptions: Array<{ label: string; value: 'all' | 'active' | 'voided' }> = [
    { label: 'All', value: 'all' },
    { label: 'Active', value: 'active' },
    { label: 'Voided', value: 'voided' },
  ];

  onSearchChange(value: string): void {
    this.emitFilters({ ...this.filters, search: value ?? '' });
  }

  onStatusChange(status: 'all' | 'active' | 'voided'): void {
    this.emitFilters({ ...this.filters, status });
  }

  onDateFromChange(value: string): void {
    this.emitFilters({ ...this.filters, fromDate: value ?? '' });
  }

  onDateToChange(value: string): void {
    this.emitFilters({ ...this.filters, toDate: value ?? '' });
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
