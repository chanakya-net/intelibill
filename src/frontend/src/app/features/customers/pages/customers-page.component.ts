import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputIconModule } from 'primeng/inputicon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SkeletonModule } from 'primeng/skeleton';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import { AddCustomerOverlayComponent } from '../components/add-customer-overlay.component';
import { CustomersFilterBarComponent } from '../components/customers-filter-bar.component';
import { CustomersTableComponent } from '../components/customers-table.component';
import { EditCustomerOverlayComponent } from '../components/edit-customer-overlay.component';
import {
  Customer,
  CustomerAccount,
  CustomerAccountSale,
  CustomerLedgerEntry,
  CustomerService,
} from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';
import { CURRENCY_ADDON_PT, CURRENCY_INPUT_GROUP_PT, CURRENCY_INPUT_NUMBER_PT } from '../../../shared/primeng-pt.config';
import { DateOnlyPipe } from '../../../shared/pipes/date-only.pipe';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

type CustomerStatusFilter = 'all' | 'active' | 'inactive';
type AccountLedgerFilter = 'all' | 'purchases' | 'payments';
type CustomerStatus = 'active' | 'inactive' | 'overdue' | 'inCredit';

interface AccountLedgerRow {
  entry: CustomerLedgerEntry;
  sale: CustomerAccountSale | null;
  reference: string;
  kind: 'purchase' | 'payment';
  debit: number | null;
  credit: number | null;
}

interface CustomerSummaryCard {
  readonly labelKey: string;
  readonly value: number;
  readonly variant: 'count' | 'money';
  readonly tone: 'amber' | 'sage' | 'terracotta' | 'ink';
}

interface CustomerDirectoryMetric {
  readonly labelKey: string;
  readonly value: number;
  readonly variant: 'count' | 'money';
}

@Component({
  selector: 'app-customers-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    AvatarModule,
    ButtonModule,
    CardModule,
    DialogModule,
    IconFieldModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputIconModule,
    InputNumberModule,
    InputTextModule,
    ProgressSpinnerModule,
    SkeletonModule,
    TagModule,
    TableModule,
    AddCustomerOverlayComponent,
    CustomersFilterBarComponent,
    CustomersTableComponent,
    EditCustomerOverlayComponent,
    DateOnlyPipe,
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
  readonly searchValue = signal('');
  readonly statusFilter = signal<CustomerStatusFilter>('all');

  readonly filteredCustomers = computed(() => {
    const search = this.searchValue().trim().toLowerCase();
    const statusFilter = this.statusFilter();

    const byStatus =
      statusFilter === 'all'
        ? [...this.customers()]
        : this.customers().filter((customer) => customer.isActive === (statusFilter === 'active'));

    if (!search) {
      return byStatus;
    }

    return byStatus.filter((customer) =>
      [customer.name, customer.phoneNumber, customer.address ?? ''].some((value) =>
        value.toLowerCase().includes(search),
      ),
    );
  });

  readonly totalCustomers = computed(() => this.customers().length);
  readonly outstandingBalance = computed(() =>
    this.sumBy(this.customers(), (customer) => Math.max(0, customer.outstandingDue ?? 0)),
  );
  readonly overdueCount = computed(
    () => this.customers().filter((customer) => (customer.outstandingDue ?? 0) > 0).length,
  );
  readonly totalCreditIssued = computed(() => this.sumBy(this.customers(), (customer) => customer.creditLimit ?? 0));
  readonly accountsWithCredit = computed(
    () => this.customers().filter((customer) => (customer.creditLimit ?? 0) > 0).length,
  );
  readonly monthlyRevenue = computed(() =>
    this.sumBy(this.customers(), (customer) => customer.currentMonthRevenue ?? 0),
  );
  readonly filteredRowCount = computed(() => this.filteredCustomers().length);
  readonly displayRowCount = computed(() => this.filteredCustomers().length);
  readonly summaryCards = computed<CustomerSummaryCard[]>(() => [
    {
      labelKey: 'customers.summary.totalCustomers',
      value: this.totalCustomers(),
      variant: 'count',
      tone: 'amber',
    },
    {
      labelKey: 'customers.summary.outstandingBalance',
      value: this.outstandingBalance(),
      variant: 'money',
      tone: 'terracotta',
    },
    {
      labelKey: 'customers.summary.overdueCount',
      value: this.overdueCount(),
      variant: 'count',
      tone: 'sage',
    },
    {
      labelKey: 'customers.summary.monthlyRevenue',
      value: this.monthlyRevenue(),
      variant: 'money',
      tone: 'ink',
    },
  ]);
  readonly directoryMetrics = computed<CustomerDirectoryMetric[]>(() => [
    {
      labelKey: 'customers.summary.totalCreditIssued',
      value: this.totalCreditIssued(),
      variant: 'money',
    },
    {
      labelKey: 'customers.summary.accountsWithCredit',
      value: this.accountsWithCredit(),
      variant: 'count',
    },
    {
      labelKey: 'customers.summary.filteredRows',
      value: this.displayRowCount(),
      variant: 'count',
    },
  ]);

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
  readonly accountLedgerFilter = signal<AccountLedgerFilter>('all');
  readonly selectedAccountInitials = computed(() => {
    const name = this.selectedAccount()?.name ?? this.selectedAccountCustomer()?.name ?? '';

    return (
      name
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((part) => part[0]?.toUpperCase())
        .join('') || 'CU'
    );
  });
  readonly selectedAccountPurchaseCount = computed(() => this.selectedAccount()?.sales.length ?? 0);
  readonly selectedAccountOverdueCount = computed(
    () => this.selectedAccount()?.sales.filter((sale) => sale.dueAmount > 0).length ?? 0,
  );
  readonly selectedAccountLifetimeRevenue = computed(() =>
    this.sumBy(this.selectedAccount()?.sales ?? [], (sale) => sale.totalAmount),
  );
  readonly selectedAccountCreditLimit = computed(() => {
    const account = this.selectedAccount();

    if (!account) {
      return 0;
    }

    return Math.max(account.outstandingDue, this.selectedAccountLifetimeRevenue());
  });
  readonly selectedAccountUsagePercent = computed(() => {
    const account = this.selectedAccount();

    if (!account) {
      return 0;
    }

    return this.customerUsagePercent({
      ...account,
      isActive: true,
      address: null,
      creditLimit: this.selectedAccountCreditLimit(),
      purchaseCount: account.sales.length,
      lifetimeRevenue: this.selectedAccountLifetimeRevenue(),
      currentMonthRevenue: 0,
    });
  });
  readonly selectedAccountOpeningBalance = computed(() => this.selectedAccount()?.ledgerEntries.at(-1)?.runningBalance ?? 0);
  readonly selectedAccountClosingBalance = computed(() => this.selectedAccount()?.outstandingDue ?? 0);
  readonly selectedAccountLedgerRows = computed<AccountLedgerRow[]>(() => {
    const account = this.selectedAccount();
    if (!account) {
      return [];
    }

    return account.ledgerEntries.map((entry) => {
      const sale = entry.saleId ? account.sales.find((item) => item.saleId === entry.saleId) ?? null : null;
      const isPayment = this.isPaymentLedgerEntry(entry, sale);
      const kind: AccountLedgerRow['kind'] = isPayment ? 'payment' : 'purchase';

      return {
        entry,
        sale,
        reference: this.accountLedgerReference(entry, sale),
        kind,
        debit: isPayment ? null : entry.amount,
        credit: isPayment ? entry.amount : null,
      };
    });
  });
  readonly selectedAccountFilteredLedgerRows = computed(() => {
    const rows = this.selectedAccountLedgerRows();
    const filter = this.accountLedgerFilter();

    if (filter === 'all') {
      return rows;
    }

    return rows.filter((row) => row.kind === (filter === 'payments' ? 'payment' : 'purchase'));
  });

  readonly paymentForm = this.fb.nonNullable.group({
    amount: [0, [Validators.required, Validators.min(0.01)]],
    paymentDate: [formatLocalIsoDate(new Date()), Validators.required],
    notes: ['', Validators.maxLength(255)],
  });

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
    this.accountLedgerFilter.set('all');
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

  onStatusFilterChange(statusFilter: CustomerStatusFilter): void {
    this.statusFilter.set(statusFilter);
  }

  onAccountLedgerFilterChange(filter: AccountLedgerFilter): void {
    this.accountLedgerFilter.set(filter);
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

  private accountLedgerReference(entry: CustomerLedgerEntry, sale: CustomerAccountSale | null): string {
    if (sale) {
      return sale.invoiceNumber;
    }

    return `PAY-${entry.entryDate.replaceAll('-', '')}`;
  }

  private isPaymentLedgerEntry(entry: CustomerLedgerEntry, sale: CustomerAccountSale | null): boolean {
    return entry.entryType === 2 || String(entry.entryType).toLowerCase() === 'paymentreceived' || sale === null;
  }

  private sumBy<T>(items: readonly T[], selector: (item: T) => number): number {
    return items.reduce((total, item) => total + selector(item), 0);
  }
}
