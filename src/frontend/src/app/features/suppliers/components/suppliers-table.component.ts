import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import { Supplier } from '../services/supplier.service';

@Component({
  selector: 'app-suppliers-table',
  standalone: true,
  imports: [CommonModule, AvatarModule, ButtonModule, CardModule, TagModule, TableModule, TranslocoPipe, CurrencyPipe],
  templateUrl: './suppliers-table.component.html',
  styleUrl: './suppliers-table.component.scss',
})
export class SuppliersTableComponent {
  @Input({ required: true }) suppliers: readonly Supplier[] = [];
  @Input() canManageSuppliers = false;
  @Input() canMakePayment = false;

  @Output() openSupplierDetail = new EventEmitter<Supplier>();
  @Output() editSupplier = new EventEmitter<Supplier>();
  @Output() makePayment = new EventEmitter<Supplier>();

  get tableSuppliers(): Supplier[] {
    return [...this.suppliers];
  }

  supplierInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  supplierAvatarColor(name: string): string {
    const colors = [
      '#b45309', '#0369a1', '#15803d', '#7c3aed',
      '#be185d', '#c2410c', '#0f766e', '#1d4ed8',
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
      return `Amount Due: ${this.formatCurrency(supplier.balanceDue)}`;
    }
    if (supplier.balanceDue < 0) {
      return `Extra Payment: ${this.formatCurrency(Math.abs(supplier.balanceDue))}`;
    }
    return 'No Balance';
  }
}
