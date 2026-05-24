import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

type CustomerStatusFilter = 'all' | 'active' | 'inactive';

interface StatusOption {
  readonly label: string;
  readonly value: CustomerStatusFilter;
}

@Component({
  selector: 'app-customers-filter-bar',
  standalone: true,
  imports: [FormsModule, IconFieldModule, InputIconModule, InputTextModule, SelectModule, TranslocoPipe],
  templateUrl: './customers-filter-bar.component.html',
  styleUrl: './customers-filter-bar.component.scss',
})
export class CustomersFilterBarComponent {
  @Input({ required: true }) searchValue = '';
  @Input({ required: true }) statusFilter: CustomerStatusFilter = 'all';

  @Output() searchValueChange = new EventEmitter<string>();
  @Output() statusFilterChange = new EventEmitter<CustomerStatusFilter>();

  readonly statusOptions: StatusOption[] = [
    { label: 'common.all', value: 'all' },
    { label: 'customers.active', value: 'active' },
    { label: 'customers.inactive', value: 'inactive' },
  ];

  onSearchChange(value: string): void {
    this.searchValueChange.emit(value);
  }

  onStatusChange(statusFilter: CustomerStatusFilter): void {
    this.statusFilterChange.emit(statusFilter);
  }

  clearSearch(): void {
    this.onSearchChange('');
  }
}
