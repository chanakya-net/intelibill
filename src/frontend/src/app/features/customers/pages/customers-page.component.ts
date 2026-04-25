import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';

import { AddCustomerOverlayComponent } from '../components/add-customer-overlay.component';
import { EditCustomerOverlayComponent } from '../components/edit-customer-overlay.component';
import { Customer, CustomerAccount, CustomerService } from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';
import { CURRENCY_ADDON_PT, CURRENCY_INPUT_GROUP_PT, CURRENCY_INPUT_NUMBER_PT } from '../../../shared/primeng-pt.config';

@Component({
  selector: 'app-customers-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    CardModule,
    AvatarModule,
    TagModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    ProgressSpinnerModule,
    TableModule,
    DialogModule,
    InputNumberModule,
    InputGroupModule,
    InputGroupAddonModule,
    AddCustomerOverlayComponent,
    EditCustomerOverlayComponent,
    TranslocoPipe,
  ],
  templateUrl: './customers-page.component.html',
  styleUrl: './customers-page.component.scss',
})
export class CustomersPageComponent {
  private readonly customersFacade = inject(CustomersFacade);
  private readonly customerService = inject(CustomerService);
  private readonly fb = inject(FormBuilder);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

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
  readonly showAccountOverlay = signal(false);
  readonly accountLoading = signal(false);
  readonly accountError = signal('');
  readonly selectedAccountCustomer = signal<Customer | null>(null);
  readonly selectedAccount = signal<CustomerAccount | null>(null);
  readonly submittingPayment = signal(false);
  readonly hasAccount = computed(() => this.selectedAccount() !== null);

  readonly paymentForm = this.fb.nonNullable.group({
    amount: [0, [Validators.required, Validators.min(0.01)]],
    paymentDate: [new Date().toISOString().slice(0, 10), Validators.required],
    notes: ['', Validators.maxLength(255)],
  });

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

  onOpenCustomerAccount(customer: Customer): void {
    this.selectedAccountCustomer.set(customer);
    this.selectedAccount.set(null);
    this.accountError.set('');
    this.showAccountOverlay.set(true);
    this.paymentForm.patchValue({ amount: 0, notes: '' });
    this.loadCustomerAccount(customer.customerId);
  }

  onCloseCustomerAccount(): void {
    this.showAccountOverlay.set(false);
    this.accountLoading.set(false);
    this.accountError.set('');
    this.selectedAccountCustomer.set(null);
    this.selectedAccount.set(null);
    this.submittingPayment.set(false);
  }

  onRecordPayment(): void {
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    const customer = this.selectedAccountCustomer();
    if (!customer) {
      return;
    }

    this.accountError.set('');
    this.submittingPayment.set(true);

    this.customerService
      .recordCustomerPayment(customer.customerId, {
        amount: Number(this.paymentForm.controls.amount.value),
        paymentDate: this.paymentForm.controls.paymentDate.value,
        notes: this.paymentForm.controls.notes.value.trim() || null,
      })
      .subscribe({
        next: () => {
          this.paymentForm.patchValue({ amount: 0, notes: '' });
          this.submittingPayment.set(false);
          this.loadCustomerAccount(customer.customerId);
          this.customersFacade.loadCustomers();
        },
        error: (error) => {
          this.accountError.set(error.error?.detail || 'customers.account.paymentFailed');
          this.submittingPayment.set(false);
        },
      });
  }

  paymentTagSeverity(entryType: number): 'success' | 'danger' | 'info' {
    return entryType === 2 ? 'success' : 'danger';
  }

  paymentTagLabel(entryType: number): string {
    return entryType === 2 ? 'customers.account.paymentReceived' : 'customers.account.saleDue';
  }

  private loadCustomerAccount(customerId: string): void {
    this.accountError.set('');
    this.accountLoading.set(true);

    this.customerService.getCustomerAccount(customerId).subscribe({
      next: (account) => {
        this.selectedAccount.set(account);
        this.accountLoading.set(false);
      },
      error: (error) => {
        this.accountError.set(error.error?.detail || 'customers.account.loadFailed');
        this.accountLoading.set(false);
      },
    });
  }
}
