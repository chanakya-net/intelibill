import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import { Customer } from '../services/customer.service';
import {
  customerStatus as deriveCustomerStatus,
  customerStatusClass as deriveCustomerStatusClass,
  customerStatusLabelKey as deriveCustomerStatusLabelKey,
  customerUsageLabel as deriveCustomerUsageLabel,
  customerUsagePercent as deriveCustomerUsagePercent,
} from '../utils/customer-status.util';

@Component({
  selector: 'app-customers-table',
  standalone: true,
  imports: [CommonModule, AvatarModule, ButtonModule, CardModule, TagModule, TableModule, TranslocoPipe],
  templateUrl: './customers-table.component.html',
  styleUrl: './customers-table.component.scss',
})
export class CustomersTableComponent {
  @Input({ required: true }) customers: readonly Customer[] = [];
  @Input() visibleRows = 0;
  @Input() totalRows = 0;

  @Output() openCustomerAccount = new EventEmitter<Customer>();
  @Output() openEditCustomer = new EventEmitter<Customer>();
  @Output() newTransaction = new EventEmitter<Customer>();

  readonly customerStatus = deriveCustomerStatus;
  readonly customerStatusLabelKey = deriveCustomerStatusLabelKey;
  readonly customerStatusClass = deriveCustomerStatusClass;
  readonly customerUsagePercent = deriveCustomerUsagePercent;
  readonly customerUsageLabel = deriveCustomerUsageLabel;

  get tableCustomers(): Customer[] {
    return [...this.customers];
  }

  onOpenCustomerAccount(customer: Customer): void {
    this.openCustomerAccount.emit(customer);
  }

  onOpenEditCustomer(customer: Customer): void {
    this.openEditCustomer.emit(customer);
  }

  onNewTransaction(customer: Customer): void {
    this.newTransaction.emit(customer);
  }

  customerInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  customerAvatarColor(name: string): string {
    const colors = ['#b45309', '#0369a1', '#15803d', '#7c3aed', '#be185d', '#c2410c', '#0f766e', '#1d4ed8'];
    let hash = 0;
    for (let index = 0; index < name.length; index++) {
      hash = name.charCodeAt(index) + ((hash << 5) - hash);
    }
    return colors[Math.abs(hash) % colors.length];
  }
}
