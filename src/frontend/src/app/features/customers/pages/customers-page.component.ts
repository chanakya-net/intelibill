import { DialogFocusDirective } from '../../../shared/accessibility/dialog-focus';
import { TillPendingActionsComponent } from '../../tills/components/till-pending-actions.component';
import { ViewChild } from '@angular/core';
import { TillPostingFieldsComponent } from '../../tills/components/till-posting-fields.component';
import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router } from '@angular/router';
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

import { AuthService } from '../../../core/auth/auth.service';
import {
  FinancialOperationIntent,
  FinancialOperationIntentService,
  classifyFinancialOperationFailure,
} from '../../../core/services/financial-operation-intent.service';
import { PendingFinancialOperationComponent } from '../../../shared/components/pending-financial-operation/pending-financial-operation.component';
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
  PaymentReceiptSummary,
  RECORDABLE_TENDER_METHODS,
  ReceiptTenderMethod,
  RecordCustomerPaymentRequest,
  customerLedgerEntryKind,
  isCustomerDebit,
  suggestOldestFirstAllocations,
} from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';
import {
  customerStatus as deriveCustomerStatus,
  customerStatusClass as deriveCustomerStatusClass,
  customerStatusLabelKey as deriveCustomerStatusLabelKey,
  customerUsageLabel as deriveCustomerUsageLabel,
  customerUsagePercent as deriveCustomerUsagePercent,
} from '../utils/customer-status.util';
import { creditPolicyLabelKey, effectiveCreditPolicy } from '../utils/credit-policy.util';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
} from '../../../shared/primeng-pt.config';
import { DateOnlyPipe } from '../../../shared/pipes/date-only.pipe';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

type CustomerStatusFilter = 'all' | 'active' | 'inactive';
type AccountLedgerFilter = 'all' | 'purchases' | 'payments';

interface AccountLedgerRow {
  entry: CustomerLedgerEntry;
  sale: CustomerAccountSale | null;
  reference: string;
  kind: 'purchase' | 'payment';
  labelKey: string;
  debit: number | null;
  credit: number | null;
}

const LEDGER_LABEL_KEYS: Record<string, string> = {
  SaleDue: 'customers.account.saleDue',
  SaleCancellationCredit: 'customers.account.saleCancellationCredit',
  PaymentReceived: 'customers.account.paymentReceived',
  ReturnCredit: 'customers.account.returnCredit',
  ReturnCreditReversal: 'customers.account.returnCreditReversal',
  PaymentReversal: 'customers.account.paymentReversal',
};

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
    DialogFocusDirective,
    TillPendingActionsComponent,
    TillPostingFieldsComponent,
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
    PendingFinancialOperationComponent,
    DateOnlyPipe,
    TranslocoPipe,
  ],
  templateUrl: './customers-page.component.html',
  styleUrl: './customers-page.component.scss',
})
export class CustomersPageComponent {
  readonly tillPaymentLocked = signal(false);
  readonly tillReversalLocked = signal(false);
  onTillPaymentLocked(locked: boolean) {
    this.tillPaymentLocked.set(locked);
    if (locked) this.paymentForm.disable({ emitEvent: false });
    else this.paymentForm.enable({ emitEvent: false });
  }
  @ViewChild('paymentTill') paymentTill!: TillPostingFieldsComponent;
  @ViewChild('reversalTill') reversalTill!: TillPostingFieldsComponent;
  private readonly customersFacade = inject(CustomersFacade);
  private readonly customerService = inject(CustomerService);
  private readonly intents = inject(FinancialOperationIntentService);
  private readonly authService = inject(AuthService);
  private readonly paymentScope = computed(() => ({
    userId: this.authService.session()?.user?.id ?? '',
    shopId: this.authService.session()?.activeShopId ?? '',
  }));
  readonly pendingNotice = signal<string | null>(null);
  readonly isCheckingPayment = signal(false);
  /** An earlier payment from this customer with no confirmed outcome; it blocks a new one. */
  readonly pendingPayment = computed(() => {
    this.intents.revision();
    const customer = this.selectedAccountCustomer();
    return customer
      ? this.intents.pending<RecordCustomerPaymentRequest>(
          this.paymentScope(),
          'customer-payment',
          customer.customerId,
        )
      : null;
  });
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  readonly customers = this.customersFacade.allCustomers;
  readonly searchValue = signal('');
  readonly statusFilter = signal<CustomerStatusFilter>('all');
  readonly customerStatus = deriveCustomerStatus;
  readonly customerStatusLabelKey = deriveCustomerStatusLabelKey;
  readonly customerStatusClass = deriveCustomerStatusClass;
  readonly customerUsagePercent = deriveCustomerUsagePercent;
  readonly customerUsageLabel = deriveCustomerUsageLabel;

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
      [customer.name, customer.phoneNumber, customer.address ?? '', customer.customerId].some(
        (value) => value.toLowerCase().includes(search),
      ),
    );
  });

  readonly totalCustomers = computed(() => this.customers().length);
  readonly outstandingBalance = computed(() =>
    this.sumBy(this.customers(), (customer) => Math.max(0, customer.outstandingDue ?? 0)),
  );
  /** Customers who owe something. Not "overdue": that needs a due date, which only the statement knows. */
  readonly outstandingCount = computed(
    () => this.customers().filter((customer) => (customer.outstandingDue ?? 0) > 0).length,
  );
  readonly totalCreditIssued = computed(() =>
    this.sumBy(this.customers(), (customer) => customer.creditLimit ?? 0),
  );
  readonly accountsWithCredit = computed(
    () => this.customers().filter((customer) => (customer.creditLimit ?? 0) > 0).length,
  );
  readonly monthlyRevenue = computed(() =>
    this.sumBy(this.customers(), (customer) => customer.currentMonthRevenue ?? 0),
  );
  readonly filteredRowCount = computed(() => this.filteredCustomers().length);
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
      labelKey: 'customers.summary.outstandingCount',
      value: this.outstandingCount(),
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
      value: this.filteredRowCount(),
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
  readonly selectedAccountLifetimeRevenue = computed(
    () => this.selectedAccountCustomer()?.lifetimeRevenue ?? 0,
  );
  /** The saved credit limit, exactly as configured; never derived from revenue or balance. */
  readonly selectedAccountCreditLimit = computed(
    () => this.selectedAccount()?.creditLimit ?? this.selectedAccountCustomer()?.creditLimit ?? 0,
  );
  readonly selectedAccountCreditPolicy = computed(() =>
    effectiveCreditPolicy(
      this.selectedAccount()?.creditPolicyMode ?? this.selectedAccountCustomer()?.creditPolicyMode,
    ),
  );
  readonly selectedAccountCreditPolicyLabelKey = computed(() =>
    creditPolicyLabelKey(this.selectedAccountCreditPolicy()),
  );
  /** Null when the limit is not enforced (legacy advisory), so nothing is promised that is not checked. */
  readonly selectedAccountAvailableCredit = computed(
    () => this.selectedAccount()?.availableCredit ?? null,
  );
  readonly selectedAccountIsActive = computed(
    () => this.selectedAccount()?.isActive ?? this.selectedAccountCustomer()?.isActive ?? true,
  );
  readonly selectedAccountOverdueAmount = computed(
    () => this.selectedAccount()?.overdueAmount ?? 0,
  );
  readonly selectedAccountUsagePercent = computed(() => {
    const account = this.selectedAccount();

    if (!account) {
      return 0;
    }

    return this.customerUsagePercent({
      outstandingDue: account.outstandingDue,
      creditLimit: this.selectedAccountCreditLimit(),
    });
  });
  /** The server's balance before the statement window; never a displayed row's after-entry balance. */
  readonly selectedAccountOpeningBalance = computed(
    () => this.selectedAccount()?.openingBalance ?? 0,
  );
  readonly selectedAccountClosingBalance = computed(
    () => this.selectedAccount()?.closingBalance ?? this.selectedAccount()?.outstandingDue ?? 0,
  );
  readonly selectedAccountLedgerRows = computed<AccountLedgerRow[]>(() => {
    const account = this.selectedAccount();
    if (!account) {
      return [];
    }

    return account.ledgerEntries.map((entry) => {
      const sale = entry.saleId
        ? (account.sales.find((item) => item.saleId === entry.saleId) ?? null)
        : null;
      const entryKind = customerLedgerEntryKind(entry.entryType);
      const kind: AccountLedgerRow['kind'] =
        entryKind === 'PaymentReceived' || entryKind === 'PaymentReversal' ? 'payment' : 'purchase';
      const debit = isCustomerDebit(entry.entryType);

      return {
        entry,
        sale,
        reference: this.accountLedgerReference(entry, sale),
        kind,
        labelKey: LEDGER_LABEL_KEYS[entryKind ?? 'SaleDue'] ?? 'customers.account.saleDue',
        debit: debit ? entry.amount : null,
        credit: debit ? null : entry.amount,
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
    method: ['Cash' as ReceiptTenderMethod, Validators.required],
    externalReference: ['', Validators.maxLength(80)],
    notes: ['', Validators.maxLength(255)],
    applyOldestFirst: [false],
  });

  readonly tenderMethods = RECORDABLE_TENDER_METHODS;
  private readonly paymentFormValue = toSignal(this.paymentForm.valueChanges, {
    initialValue: this.paymentForm.getRawValue(),
  });

  /** Invoices still owed today, oldest first, with what payments and returns already settled. */
  readonly selectedAccountOpenInvoices = computed(() =>
    [...(this.selectedAccount()?.sales ?? [])]
      .filter((sale) => (sale.currentOutstandingAmount ?? 0) > 0)
      .sort((a, b) => a.soldAt.localeCompare(b.soldAt)),
  );
  readonly selectedAccountUnallocatedCredit = computed(
    () => this.selectedAccount()?.unallocatedCredit ?? 0,
  );
  readonly selectedAccountReceipts = computed<readonly PaymentReceiptSummary[]>(
    () => this.selectedAccount()?.receipts ?? [],
  );

  /** The oldest-first allocation the operator may accept; nothing is allocated unless they tick it. */
  readonly allocationSuggestion = computed(() => {
    const amount = Number(this.paymentFormValue().amount ?? 0);
    const sales = this.selectedAccount()?.sales ?? [];
    const allocations = amount > 0 ? suggestOldestFirstAllocations(sales, amount) : [];
    const allocated = allocations.reduce((total, allocation) => total + allocation.amount, 0);
    const invoiceNumbers = new Map(sales.map((sale) => [sale.saleId, sale.invoiceNumber]));
    return {
      allocations,
      rows: allocations.map((allocation) => ({
        ...allocation,
        invoiceNumber: invoiceNumbers.get(allocation.targetId) ?? '',
      })),
      leftover: Math.max(0, Math.round((amount - allocated) * 100) / 100),
    };
  });

  readonly isOwner = computed(() => {
    const session = this.authService.session();
    return session?.shops?.find((shop) => shop.shopId === session.activeShopId)?.role === 'Owner';
  });
  readonly reversingReceiptId = signal<string | null>(null);
  readonly reversalReason = signal('');
  readonly submittingReversal = signal(false);
  readonly reversalError = signal('');
  /** One key per receipt until a reversal is confirmed, so a retry after a timeout replays instead of conflicting. */
  private readonly reversalKeys = new Map<string, string>();

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

  onOpenNewTransaction(customer: Customer): void {
    void this.router.navigate(['/sales/new'], {
      queryParams: {
        customerId: customer.customerId,
      },
    });
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
    this.paymentForm.patchValue({
      amount: 0,
      notes: '',
      externalReference: '',
      applyOldestFirst: false,
    });
    this.reversingReceiptId.set(null);
    this.loadCustomerAccount(customer.customerId);
  }

  onCloseCustomerAccount(): void {
    if (this.submittingPayment() || this.submittingReversal() || this.isCheckingPayment()) return;
    this.showAccountOverlay.set(false);
    this.accountLoading.set(false);
    this.accountError.set('');
    this.selectedAccountCustomer.set(null);
    this.selectedAccount.set(null);
  }

  onRecordPayment(): void {
    if (this.tillPaymentLocked()) return;
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    const customer = this.selectedAccountCustomer();
    if (!customer) {
      return;
    }

    this.accountError.set('');
    if (this.pendingPayment()) {
      this.accountError.set('financialOperations.pendingBlocksNew');
      return;
    }

    let intent: FinancialOperationIntent<RecordCustomerPaymentRequest>;
    try {
      const controls = this.paymentForm.controls;
      intent = this.intents.prepare(this.paymentScope(), 'customer-payment', customer.customerId, {
        ...this.paymentTill.sessionOnly(),
        amount: Number(controls.amount.value),
        paymentDate: controls.paymentDate.value,
        notes: controls.notes.value.trim() || null,
        method: controls.method.value,
        externalReference: controls.externalReference.value.trim() || null,
        allocations: controls.applyOldestFirst.value ? this.allocationSuggestion().allocations : [],
      });
    } catch {
      this.accountError.set('financialOperations.persistFailed');
      return;
    }

    this.sendPayment(customer, intent);
  }

  onRetryPendingPayment(): void {
    const customer = this.selectedAccountCustomer();
    const intent = this.pendingPayment();
    if (!customer || !intent) {
      return;
    }
    this.accountError.set('');
    this.pendingNotice.set(null);
    this.sendPayment(customer, intent);
  }

  onCheckPendingPayment(): void {
    const customer = this.selectedAccountCustomer();
    const intent = this.pendingPayment();
    if (!customer || !intent) {
      return;
    }
    this.isCheckingPayment.set(true);
    this.intents.check(this.paymentScope(), intent).subscribe({
      next: (committed) => {
        this.isCheckingPayment.set(false);
        this.pendingNotice.set(
          committed ? 'financialOperations.confirmed' : 'financialOperations.notYetRecorded',
        );
        if (committed) {
          this.loadCustomerAccount(customer.customerId);
          this.customersFacade.loadCustomers();
        }
      },
      error: () => {
        this.isCheckingPayment.set(false);
        this.pendingNotice.set('financialOperations.checkFailed');
      },
    });
  }

  private sendPayment(
    customer: Customer,
    intent: FinancialOperationIntent<RecordCustomerPaymentRequest>,
  ): void {
    this.submittingPayment.set(true);
    this.intents
      .send(this.paymentScope(), intent, (payload, operationKey) =>
        this.customerService.recordCustomerPayment(customer.customerId, {
          ...payload,
          operationKey,
        }),
      )
      .subscribe({
        next: () => {
          this.paymentForm.patchValue({
            amount: 0,
            notes: '',
            externalReference: '',
            applyOldestFirst: false,
          });
          this.submittingPayment.set(false);
          this.loadCustomerAccount(customer.customerId);
          this.customersFacade.loadCustomers();
        },
        error: (error) => {
          this.accountError.set(
            classifyFinancialOperationFailure(error) === 'uncertain'
              ? 'financialOperations.outcomeUnknown'
              : error.error?.detail || 'customers.account.paymentFailed',
          );
          this.submittingPayment.set(false);
        },
      });
  }

  onStartReversal(receipt: PaymentReceiptSummary): void {
    this.reversalError.set('');
    this.reversalReason.set('');
    this.reversingReceiptId.set(receipt.receiptId);
  }

  onCancelReversal(): void {
    this.reversingReceiptId.set(null);
    this.reversalReason.set('');
    this.reversalError.set('');
  }

  onConfirmReversal(): void {
    if (this.tillReversalLocked()) return;
    const customer = this.selectedAccountCustomer();
    const receiptId = this.reversingReceiptId();
    const reason = this.reversalReason().trim();
    if (!customer || !receiptId) {
      return;
    }
    if (reason.length < 3) {
      this.reversalError.set('customers.account.reversalReasonRequired');
      return;
    }

    const operationKey = this.reversalKeys.get(receiptId) ?? `reverse-${crypto.randomUUID()}`;
    this.reversalKeys.set(receiptId, operationKey);
    let evidence;
    try {
      evidence = this.reversalTill.fields(
        this.selectedAccount()?.receipts?.find((x) => x.receiptId === receiptId)?.amount ?? 0,
      );
    } catch {
      this.reversalError.set('tills.evidenceRequired');
      return;
    }
    this.submittingReversal.set(true);
    this.customerService
      .reversePayment(receiptId, { reason, operationKey, ...evidence })
      .subscribe({
        next: () => {
          this.reversalKeys.delete(receiptId);
          this.submittingReversal.set(false);
          this.onCancelReversal();
          this.loadCustomerAccount(customer.customerId);
          this.customersFacade.loadCustomers();
        },
        error: (error) => {
          this.submittingReversal.set(false);
          if (classifyFinancialOperationFailure(error) !== 'uncertain') {
            // A definite refusal: a later attempt is a new decision with its own key.
            this.reversalKeys.delete(receiptId);
          }
          this.reversalError.set(
            classifyFinancialOperationFailure(error) === 'uncertain'
              ? 'financialOperations.outcomeUnknown'
              : error.error?.detail || 'customers.account.reversalFailed',
          );
        },
      });
  }

  tenderLabelKey(method: ReceiptTenderMethod): string {
    return `customers.account.tender.${method}`;
  }

  onStatusFilterChange(statusFilter: CustomerStatusFilter): void {
    this.statusFilter.set(statusFilter);
  }

  onAccountLedgerFilterChange(filter: AccountLedgerFilter): void {
    this.accountLedgerFilter.set(filter);
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

  private accountLedgerReference(
    entry: CustomerLedgerEntry,
    sale: CustomerAccountSale | null,
  ): string {
    if (sale) {
      return sale.invoiceNumber;
    }

    return `PAY-${entry.entryDate.replaceAll('-', '')}`;
  }

  private sumBy<T>(items: readonly T[], selector: (item: T) => number): number {
    return items.reduce((total, item) => total + selector(item), 0);
  }
}
