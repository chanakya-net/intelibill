import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import type { ItemCatalogStatusFilter } from '../services/inventory.models';

interface StatusOption {
  readonly label: string;
  readonly value: ItemCatalogStatusFilter;
}

@Component({
  selector: 'app-inventory-filter-bar',
  standalone: true,
  imports: [FormsModule, IconFieldModule, InputIconModule, InputTextModule, SelectModule, TranslocoPipe],
  templateUrl: './inventory-filter-bar.component.html',
  styleUrl: './inventory-filter-bar.component.scss',
})
export class InventoryFilterBarComponent {
  @Input({ required: true }) searchValue = '';
  @Input({ required: true }) statusFilter: ItemCatalogStatusFilter = 'all';

  @Output() searchValueChange = new EventEmitter<string>();
  @Output() statusFilterChange = new EventEmitter<ItemCatalogStatusFilter>();

  readonly statusOptions: StatusOption[] = [
    { label: 'common.all', value: 'all' },
    { label: 'inventory.active', value: 'active' },
    { label: 'inventory.inactive', value: 'inactive' },
    { label: 'inventory.inStock', value: 'inStock' },
    { label: 'inventory.reorder', value: 'runningLow' },
    { label: 'inventory.outOfStock', value: 'critical' },
  ];

  onSearchChange(value: string): void {
    this.searchValueChange.emit(value);
  }

  onStatusChange(statusFilter: ItemCatalogStatusFilter): void {
    this.statusFilterChange.emit(statusFilter);
  }

  clearSearch(): void {
    this.onSearchChange('');
  }
}
