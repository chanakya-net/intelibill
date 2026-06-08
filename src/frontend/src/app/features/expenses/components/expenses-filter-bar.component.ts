import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

export type ExpenseStatusFilter = 'all' | 'active' | 'voided';

interface StatusOption {
  readonly label: string;
  readonly value: ExpenseStatusFilter;
}

@Component({
  selector: 'app-expenses-filter-bar',
  standalone: true,
  imports: [FormsModule, IconFieldModule, InputIconModule, InputTextModule, SelectModule, TranslocoPipe],
  templateUrl: './expenses-filter-bar.component.html',
  styleUrl: './expenses-filter-bar.component.scss',
})
export class ExpensesFilterBarComponent {
  @Input({ required: true }) searchValue = '';
  @Input({ required: true }) statusFilter: ExpenseStatusFilter = 'all';

  @Output() searchValueChange = new EventEmitter<string>();
  @Output() statusFilterChange = new EventEmitter<ExpenseStatusFilter>();

  readonly statusOptions: StatusOption[] = [
    { label: 'common.all', value: 'all' },
    { label: 'expenses.active', value: 'active' },
    { label: 'expenses.voided', value: 'voided' },
  ];

  onSearchChange(value: string): void {
    this.searchValueChange.emit(value);
  }

  onStatusChange(statusFilter: ExpenseStatusFilter): void {
    this.statusFilterChange.emit(statusFilter);
  }

  clearSearch(): void {
    this.onSearchChange('');
  }
}
