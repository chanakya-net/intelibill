import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

type SupplierStatusFilter = 'all' | 'active' | 'inactive';

interface StatusOption {
  readonly label: string;
  readonly value: SupplierStatusFilter;
}

@Component({
  selector: 'app-suppliers-filter-bar',
  standalone: true,
  imports: [FormsModule, IconFieldModule, InputIconModule, InputTextModule, SelectModule, TranslocoPipe],
  templateUrl: './suppliers-filter-bar.component.html',
  styleUrl: './suppliers-filter-bar.component.scss',
})
export class SuppliersFilterBarComponent {
  @Input({ required: true }) searchValue = '';
  @Input({ required: true }) statusFilter: SupplierStatusFilter = 'all';

  @Output() searchValueChange = new EventEmitter<string>();
  @Output() statusFilterChange = new EventEmitter<SupplierStatusFilter>();

  readonly statusOptions: StatusOption[] = [
    { label: 'common.all', value: 'all' },
    { label: 'suppliers.active', value: 'active' },
    { label: 'suppliers.inactive', value: 'inactive' },
  ];

  onSearchChange(value: string): void {
    this.searchValueChange.emit(value);
  }

  onStatusChange(statusFilter: SupplierStatusFilter): void {
    this.statusFilterChange.emit(statusFilter);
  }

  clearSearch(): void {
    this.onSearchChange('');
  }
}
