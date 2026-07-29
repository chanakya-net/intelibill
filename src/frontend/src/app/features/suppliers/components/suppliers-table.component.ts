import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import { Supplier } from '../services/supplier.service';
import { SuppliersFilterBarComponent } from './suppliers-filter-bar.component';

@Component({
  selector: 'app-suppliers-table',
  standalone: true,
  imports: [
    CommonModule,
    AvatarModule,
    ButtonModule,
    CardModule,
    TagModule,
    TableModule,
    TranslocoPipe,
    CurrencyPipe,
    SuppliersFilterBarComponent,
  ],
  templateUrl: './suppliers-table.component.html',
  styleUrl: './suppliers-table.component.scss',
})
export class SuppliersTableComponent {
  private readonly transloco = inject(TranslocoService);

  @Input({ required: true }) suppliers: readonly Supplier[] = [];
  @Input() canManageSuppliers = false;
  @Input() canMakePayment = false;

  @Output() openSupplierDetail = new EventEmitter<Supplier>();
  @Output() editSupplier = new EventEmitter<Supplier>();
  @Output() makePayment = new EventEmitter<Supplier>();

  readonly searchValue = signal('');
  readonly statusFilter = signal<'all' | 'active' | 'inactive'>('all');

  readonly filteredSuppliers = computed(() => {
    let result = [...this.suppliers];
    const search = this.searchValue().toLowerCase();
    const status = this.statusFilter();

    if (search) {
      result = result.filter(
        (s) =>
          s.name.toLowerCase().includes(search) ||
          s.contactPersonName?.toLowerCase().includes(search) ||
          s.contactPersonPhone?.toLowerCase().includes(search) ||
          s.city.toLowerCase().includes(search),
      );
    }

    if (status === 'active') {
      result = result.filter((s) => s.isActive);
    } else if (status === 'inactive') {
      result = result.filter((s) => !s.isActive);
    }

    return result;
  });

  get tableSuppliers(): Supplier[] {
    return this.filteredSuppliers();
  }

  onSearchChange(value: string): void {
    this.searchValue.set(value);
  }

  onStatusChange(value: 'all' | 'active' | 'inactive'): void {
    this.statusFilter.set(value);
  }

  supplierInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  supplierAvatarColor(name: string): string {
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

  onOpenSupplierDetail(supplier: Supplier): void {
    this.openSupplierDetail.emit(supplier);
  }

  onOpenEditSupplier(supplier: Supplier): void {
    this.editSupplier.emit(supplier);
  }

  onOpenMakePayment(supplier: Supplier): void {
    this.makePayment.emit(supplier);
  }

  formatCurrency(amount: number): string {
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
    }).format(amount);
  }

  getBalanceLabel(supplier: Supplier): string {
    if (supplier.balanceDue > 0) {
      return this.transloco.translate('suppliers.balanceLabels.amountDue', {
        amount: this.formatCurrency(supplier.balanceDue),
      });
    }
    if (supplier.balanceDue < 0) {
      return this.transloco.translate('suppliers.balanceLabels.extraPayment', {
        amount: this.formatCurrency(Math.abs(supplier.balanceDue)),
      });
    }
    return this.transloco.translate('suppliers.balanceLabels.none');
  }
}
