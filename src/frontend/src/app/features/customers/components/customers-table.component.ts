import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import { Customer } from '../services/customer.service';

type CustomerStatus = 'active' | 'inactive' | 'overdue' | 'inCredit';

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

  customerStatus(customer: Customer): CustomerStatus {
    if (!customer.isActive) {
      return 'inactive';
    }

    const outstandingDue = customer.outstandingDue ?? 0;

    if (outstandingDue > 0) {
      return 'overdue';
    }

    if (outstandingDue < 0) {
      return 'inCredit';
    }

    return 'active';
  }

  customerStatusLabelKey(customer: Customer): string {
    return `customers.statuses.${this.customerStatus(customer)}`;
  }

  customerStatusClass(customer: Customer): string {
    const status = this.customerStatus(customer);
    return `status-badge--${status === 'inCredit' ? 'in-credit' : status}`;
  }

  customerUsagePercent(customer: Customer): number {
    const creditLimit = customer.creditLimit ?? 0;

    if (creditLimit <= 0) {
      return 0;
    }

    const usage = (Math.max(0, customer.outstandingDue ?? 0) / creditLimit) * 100;
    return Math.min(100, Math.max(0, Math.round(usage)));
  }

  customerUsageLabel(customer: Customer): string {
    return `${this.customerUsagePercent(customer)}%`;
  }
}
