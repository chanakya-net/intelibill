import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { AvatarModule } from 'primeng/avatar';
import { TagModule } from 'primeng/tag';
import { PaginatorModule } from 'primeng/paginator';
import { TableModule } from 'primeng/table';

import type { Item } from '../services/inventory.models';

@Component({
  selector: 'app-inventory-table',
  standalone: true,
  imports: [
    CommonModule,
    AvatarModule,
    BadgeModule,
    ButtonModule,
    CardModule,
    PaginatorModule,
    TagModule,
    TableModule,
    TranslocoPipe,
  ],
  templateUrl: './inventory-table.component.html',
  styleUrl: './inventory-table.component.scss',
})
export class InventoryTableComponent {
  @Input({ required: true }) items: readonly Item[] = [];
  @Input() canManageInventory = false;
  @Input() isSubmitting = false;
  @Input() pageNumber = 1;
  @Input() pageSize = 20;
  @Input() totalCount = 0;
  @Input() footerStart = 0;
  @Input() footerEnd = 0;

  @Input() selectedItems: readonly Item[] = [];

  @Output() selectedItemsChange = new EventEmitter<readonly Item[]>();
  @Output() printLabelRequested = new EventEmitter<Item>();

  @Output() selectItem = new EventEmitter<Item>();
  @Output() editItem = new EventEmitter<Item>();
  @Output() pageChange = new EventEmitter<{ page: number; rows: number }>();

  selected = new Set<string>();

  get tableItems(): Item[] {
    return [...this.items];
  }

  ngOnChanges(): void {
    this.selected = new Set((this.selectedItems ?? []).map((item) => item.id));
  }

  onOpenItem(item: Item): void {
    this.selectItem.emit(item);
  }

  onEditItem(item: Item): void {
    this.editItem.emit(item);
  }

  onPrintLabel(item: Item): void {
    this.printLabelRequested.emit(item);
  }

  onSelectionChange(nextSelection: readonly Item[]): void {
    this.selected = new Set((nextSelection ?? []).map((item) => item.id));
    this.selectedItems = [...(nextSelection ?? [])];
    this.selectedItemsChange.emit([...nextSelection]);
  }

  toggleSelection(item: Item): void {
    const next = new Set(this.selected);
    if (next.has(item.id)) {
      next.delete(item.id);
    } else {
      next.add(item.id);
    }
    this.selected = next;

    const selectedItems = (this.selectedItems ?? []).filter((existing) => next.has(existing.id));
    const alreadySelectedIds = new Set(selectedItems.map((existing) => existing.id));
    const missing = this.items.filter(
      (candidate) => next.has(candidate.id) && !alreadySelectedIds.has(candidate.id),
    );
    this.selectedItemsChange.emit([...selectedItems, ...missing]);
  }

  isSelected(item: Item): boolean {
    return this.selected.has(item.id);
  }

  onPageChange(event: { first?: number; page?: number; rows?: number }): void {
    const rows = event.rows ?? this.pageSize;
    const isPageSizeChange = rows !== this.pageSize;
    const first = event.first ?? (event.page ?? 0) * rows;
    const page = isPageSizeChange ? 1 : Math.floor(first / rows) + 1;
    this.pageChange.emit({ page, rows });
  }

  stockStatusSeverity(status: Item['stockStatus']): 'danger' | 'warn' | 'success' | 'secondary' {
    if (status === 'inactive') return 'secondary';
    if (status === 'critical') return 'danger';
    if (status === 'runningLow') return 'warn';
    return 'success';
  }

  productInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  productAvatarColor(name: string): string {
    const colors = [
      '#b45309',
      '#0369a1',
      '#15803d',
      '#7c3aed',
      '#be185d',
      '#c2410c',
      '#0f766e',
      '#1d4ed8',
    ];
    let hash = 0;
    for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return colors[Math.abs(hash) % colors.length];
  }

  stockSeverity(stock: number): 'danger' | 'warn' | 'success' {
    if (stock <= 5) return 'danger';
    if (stock < 50) return 'warn';
    return 'success';
  }

  stockStatusLabelKey(stockStatus: Item['stockStatus']): string {
    if (stockStatus === 'inactive') {
      return 'inventory.inactive';
    }

    if (stockStatus === 'runningLow') {
      return 'inventory.reorder';
    }

    if (stockStatus === 'critical') {
      return 'inventory.outOfStock';
    }

    return 'inventory.inStock';
  }
}
