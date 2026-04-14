import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { AvatarModule } from 'primeng/avatar';
import { TagModule } from 'primeng/tag';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { AddCustomerOverlayComponent } from '../components/add-customer-overlay.component';
import { EditCustomerOverlayComponent } from '../components/edit-customer-overlay.component';
import { Customer } from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';

@Component({
  selector: 'app-customers-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CardModule,
    AvatarModule,
    TagModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    ProgressSpinnerModule,
    TableModule,
    AddCustomerOverlayComponent,
    EditCustomerOverlayComponent,
    TranslocoPipe,
  ],
  templateUrl: './customers-page.component.html',
  styleUrl: './customers-page.component.scss',
})
export class CustomersPageComponent {
  private readonly customersFacade = inject(CustomersFacade);

  readonly customers = this.customersFacade.allCustomers;
  readonly tableCustomers = computed(() => [...this.customers()]);
  readonly searchValue = signal('');
  readonly filteredCustomers = computed(() => {
    const q = this.searchValue().toLowerCase();
    if (!q) return [...this.customers()];
    return this.customers().filter(
      (c) =>
        c.name.toLowerCase().includes(q) ||
        c.phoneNumber.toLowerCase().includes(q) ||
        (c.address ?? '').toLowerCase().includes(q),
    );
  });
  readonly isLoading = this.customersFacade.loadingCustomers;
  readonly serverError = this.customersFacade.errorMessage;
  readonly lastMutationType = this.customersFacade.lastMutationType;
  readonly lastMutationSucceeded = this.customersFacade.lastMutationSucceeded;

  readonly showAddCustomerOverlay = signal(false);
  readonly showEditCustomerOverlay = signal(false);
  readonly editingCustomer = signal<Customer | null>(null);

  customerInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  customerAvatarColor(name: string): string {
    const colors = [
      '#b45309', '#0369a1', '#15803d', '#7c3aed',
      '#be185d', '#c2410c', '#0f766e', '#1d4ed8',
    ];
    let hash = 0;
    for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return colors[Math.abs(hash) % colors.length];
  }

  constructor() {
    this.customersFacade.loadCustomers();

    effect(() => {
      if (!this.lastMutationSucceeded()) {
        return;
      }

      const mutationType = this.lastMutationType();
      if (mutationType === 'add-customer' && this.showAddCustomerOverlay()) {
        this.showAddCustomerOverlay.set(false);
        this.customersFacade.clearMutationStatus();
        return;
      }

      if (mutationType === 'edit-customer' && this.showEditCustomerOverlay()) {
        this.showEditCustomerOverlay.set(false);
        this.editingCustomer.set(null);
        this.customersFacade.clearMutationStatus();
        return;
      }
    });
  }

  onOpenAddCustomer(): void {
    this.customersFacade.clearError();
    this.customersFacade.clearMutationStatus();
    this.showAddCustomerOverlay.set(true);
  }

  onCloseAddCustomer(): void {
    this.showAddCustomerOverlay.set(false);
  }

  onOpenEditCustomer(customer: Customer): void {
    this.customersFacade.clearError();
    this.customersFacade.clearMutationStatus();
    this.editingCustomer.set(customer);
    this.showEditCustomerOverlay.set(true);
  }

  onCloseEditCustomer(): void {
    this.showEditCustomerOverlay.set(false);
    this.editingCustomer.set(null);
  }
}
