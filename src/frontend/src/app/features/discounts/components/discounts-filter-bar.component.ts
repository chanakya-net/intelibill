import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';

import { DiscountRuleType } from '../services/discount.service';

export type DiscountStatusFilter = 'active' | 'disabled' | 'expired' | 'all';
export type DiscountSortOption = 'created_desc' | 'created_asc' | 'name_asc' | 'name_desc';

interface SelectOption<T> {
  readonly label: string;
  readonly value: T;
}

@Component({
  selector: 'app-discounts-filter-bar',
  standalone: true,
  imports: [
    FormsModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './discounts-filter-bar.component.html',
  styleUrl: './discounts-filter-bar.component.scss',
})
export class DiscountsFilterBarComponent {
  @Input({ required: true }) searchValue = '';
  @Input({ required: true }) statusFilter: DiscountStatusFilter = 'active';
  @Input({ required: true }) ruleTypeFilter: DiscountRuleType | '' = '';
  @Input({ required: true }) sortValue: DiscountSortOption = 'created_desc';

  @Output() searchValueChange = new EventEmitter<string>();
  @Output() statusFilterChange = new EventEmitter<DiscountStatusFilter>();
  @Output() ruleTypeFilterChange = new EventEmitter<DiscountRuleType | ''>();
  @Output() sortValueChange = new EventEmitter<DiscountSortOption>();

  readonly statusOptions: SelectOption<DiscountStatusFilter>[] = [
    { label: 'discounts.filters.status.active', value: 'active' },
    { label: 'discounts.filters.status.disabled', value: 'disabled' },
    { label: 'discounts.filters.status.expired', value: 'expired' },
    { label: 'discounts.filters.status.all', value: 'all' },
  ];

  readonly ruleTypeOptions: SelectOption<DiscountRuleType | ''>[] = [
    { label: 'discounts.filters.type.all', value: '' },
    { label: 'discounts.ruleType.BatchPercentage', value: 'BatchPercentage' },
    { label: 'discounts.ruleType.SalePercentage', value: 'SalePercentage' },
    { label: 'discounts.ruleType.SaleThresholdPercentage', value: 'SaleThresholdPercentage' },
  ];

  readonly sortOptions: SelectOption<DiscountSortOption>[] = [
    { label: 'discounts.filters.sort.createdDesc', value: 'created_desc' },
    { label: 'discounts.filters.sort.createdAsc', value: 'created_asc' },
    { label: 'discounts.filters.sort.nameAsc', value: 'name_asc' },
    { label: 'discounts.filters.sort.nameDesc', value: 'name_desc' },
  ];

  onSearchChange(value: string): void {
    this.searchValueChange.emit(value);
  }

  onStatusChange(value: DiscountStatusFilter): void {
    this.statusFilterChange.emit(value);
  }

  onRuleTypeChange(value: DiscountRuleType | ''): void {
    this.ruleTypeFilterChange.emit(value);
  }

  onSortChange(value: DiscountSortOption): void {
    this.sortValueChange.emit(value);
  }

  clearSearch(): void {
    this.onSearchChange('');
  }
}
