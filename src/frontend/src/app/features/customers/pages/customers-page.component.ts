import { DefinitionAccessNoticeComponent } from '../../billing/status/definition-access-notice.component';
import { OperatingAccessHintService } from '../../billing/status/operating-access-hint.service';
import { DestroyRef } from '@angular/core';
import {
  DEFAULT_DIRECTORY_QUERY,
  EMPTY_CUSTOMER_SUMMARY,
} from '../../../shared/models/party-directory.models';
import { signal as errorSignal } from '@angular/core';
import {
  ApiErrorMessageService,
  type LocalizedError,
} from '../../../core/i18n/api-error-message.service';
import { LocaleInstantPipe } from '../../../shared/pipes/locale-instant.pipe';
import { LocaleBusinessDatePipe } from '../../../shared/pipes/locale-business-date.pipe';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
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

import {
  PaymentSettlementPreviewService,
  SETTLEMENT_ERROR_KEYS,
  canSubmitPayment,
  shopPaymentDate,
} from '../../../core/services/payment-settlement-preview.service';
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
    LocaleMoneyPipe,
    LocaleBusinessDatePipe,
    LocaleInstantPipe,
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
    DefinitionAccessNoticeComponent,
    CustomersFilterBarComponent,
    CustomersTableComponent,
    EditCustomerOverlayComponent,
    PendingFinancialOperationComponent,
    TranslocoPipe,
  ],
  templateUrl: './customers-page.component.html',
  styleUrl: './customers-page.component.scss',
})
export class CustomersPageComponent {
  readonly tillPaymentLocked = signal(false);
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
  readonly searchValue = signal(this.customersFacade.query?.()?.search ?? '');
  readonly statusFilter = signal<CustomerStatusFilter>(
    this.customersFacade.query?.()?.status ?? 'all',
  );
  readonly customerStatus = deriveCustomerStatus;
  readonly customerStatusLabelKey = deriveCustomerStatusLabelKey;
  readonly customerStatusClass = deriveCustomerStatusClass;
  readonly customerUsagePercent = deriveCustomerUsagePercent;
  readonly customerUsageLabel = deriveCustomerUsageLabel;

  readonly filteredCustomers = computed(() => this.customers());
  readonly directoryQuery = computed(
    () => this.customersFacade.query?.() ?? DEFAULT_DIRECTORY_QUERY,
  );
  readonly pageNumber = computed(() => this.directoryQuery().pageNumber ?? 1);
  readonly pageSize = computed(() => this.directoryQuery().pageSize ?? 20);
  readonly totalCount = computed(() => this.customersFacade.totalCount?.() ?? 0);
  readonly directorySummary = computed(
    () => this.customersFacade.summary?.() ?? EMPTY_CUSTOMER_SUMMARY,
  );
  readonly totalCustomers = computed(() => this.directorySummary().totalCustomers);
  readonly outstandingBalance = computed(() => this.directorySummary().outstandingBalance);
  readonly outstandingCount = computed(() => this.directorySummary().outstandingCount);
  readonly totalCreditIssued = computed(() => this.directorySummary().totalCreditIssued);
  readonly accountsWithCredit = computed(() => this.directorySummary().accountsWithCredit);
  readonly monthlyRevenue = computed(() => this.directorySummary().monthlyRevenue);
  readonly filteredRowCount = this.totalCount;
  private directorySearchTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly directoryDestroy = inject(DestroyRef).onDestroy(() =>
    clearTimeout(this.directorySearchTimer),
  );
  onSearchChange(value: string): void {
    this.searchValue.set(value);
    clearTimeout(this.directorySearchTimer);
    this.directorySearchTimer = setTimeout(() => this.loadDirectoryPage(1), 250);
  }
  onDirectoryPage(event: { page: number; rows: number }): void {
    clearTimeout(this.directorySearchTimer);
    this.loadDirectoryPage(event.page, event.rows);
  }
  private loadDirectoryPage(pageNumber: number, pageSize = this.pageSize()): void {
    this.customersFacade.loadDirectory({
      search: this.searchValue().trim(),
      status: this.statusFilter(),
      pageNumber,
      pageSize,
    });
  }
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

  readonly definitionAccess = inject(OperatingAccessHintService);
  readonly showAddCustomerOverlay = signal(false);
  readonly showEditCustomerOverlay = signal(false);
  readonly editingCustomer = signal<Customer | null>(null);
  readonly showAccountOverlay = signal(false);
  readonly accountLoading = signal(false);
  private readonly apiErrors = inject(ApiErrorMessageService);
  readonly localizedError =
    this.customersFacade.localizedError ?? errorSignal<LocalizedError | undefined>(undefined);
  readonly accountLocalizedError = signal<LocalizedError | undefined>(undefined);
  readonly reversalLocalizedError = signal<LocalizedError | undefined>(undefined);
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
    paymentDate: [shopPaymentDate(), Validators.required],
    method: ['Cash' as ReceiptTenderMethod, Validators.required],
    externalReference: ['', Validators.maxLength(80)],
    notes: ['', Validators.maxLength(255)],
    applyOldestFirst: [false],
  });

  readonly tenderMethods = RECORDABLE_TENDER_METHODS;
  private readonly paymentFormValue = toSignal(this.paymentForm.valueChanges, {
    initialValue: this.paymentForm.getRawValue(),
  });

  private readonly settlementRefresh = signal(0);
  readonly settlementPreview = inject(PaymentSettlementPreviewService).watch(
    'Customer',
    computed(() => this.selectedAccountCustomer()?.customerId ?? ''),
    computed(() => this.paymentFormValue().paymentDate ?? ''),
    this.settlementRefresh,
  );
  readonly settlementLimit = computed(() => {
    const preview = this.settlementPreview();
    return preview.status === 'ready' ? preview.limit : null;
  });
  readonly paymentAllowed = computed(() =>
    canSubmitPayment(this.settlementPreview(), Number(this.paymentFormValue().amount ?? 0)),
  );

  refreshSettlementPreview(): void {
    this.settlementRefresh.update((revision) => revision + 1);
  }

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
    const sales = (this.selectedAccount()?.sales ?? []).filter((sale) =>
      this.settlementLimit() === null ||
      shopPaymentDate(new Date(sale.soldAt)) <= (this.paymentFormValue().paymentDate ?? ''),
    );
    const allocations = amount > 0 && this.paymentAllowed() ? suggestOldestFirstAllocations(sales, amount) : [];
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
  readonly accountReceiptIds = computed(() => this.selectedAccountReceipts().map((receipt) => receipt.receiptId));
  readonly lockedReversalIds = signal<readonly string[]>([]);

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
    if (this.definitionAccess.blockingMessage()) return;
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

  accountAmountInputPt() {
    return {
      ...this.currencyInputPt,
      pcInputText: {
        ...this.currencyInputPt.pcInputText,
        root: {
          ...this.currencyInputPt.pcInputText?.root,
          'aria-invalid': this.accountFieldError('amount') ? 'true' : undefined,
        },
      },
    };
  }

  onOpenCustomerAccount(customer: Customer): void {
    this.pendingNotice.set(null);
    this.selectedAccountCustomer.set(customer);
    this.selectedAccount.set(null);
    this.accountError.set('');
    this.accountLocalizedError.set(undefined);
    this.accountLedgerFilter.set('all');
    this.showAccountOverlay.set(true);
    this.paymentForm.patchValue({
      amount: 0,
      paymentDate: shopPaymentDate(),
      notes: '',
      externalReference: '',
      applyOldestFirst: false,
    });
    this.reversingReceiptId.set(null);
    this.lockedReversalIds.set([]);
    this.loadCustomerAccount(customer.customerId);
  }

  onCloseCustomerAccount(): void {
    if (this.submittingPayment() || this.submittingReversal() || this.isCheckingPayment()) return;
    this.showAccountOverlay.set(false);
    this.accountLoading.set(false);
    this.accountError.set('');
    this.accountLocalizedError.set(undefined);
    this.selectedAccountCustomer.set(null);
    this.selectedAccount.set(null);
  }

  onRecordPayment(): void {
    if (this.tillPaymentLocked() || !this.paymentAllowed()) return;
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    const customer = this.selectedAccountCustomer();
    if (!customer) {
      return;
    }

    this.accountError.set('');
    this.accountLocalizedError.set(undefined);
    if (this.pendingPayment()) {
      this.accountError.set('financialOperations.pendingBlocksNew');
      return;
    }

    this.pendingNotice.set(null);
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
    this.accountLocalizedError.set(undefined);
    this.pendingNotice.set(null);
    this.sendPayment(customer, intent, true);
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
    retry = false,
  ): void {
    this.submittingPayment.set(true);
    const send = (payload: RecordCustomerPaymentRequest, operationKey: string) =>
      this.customerService.recordCustomerPayment(customer.customerId, { ...payload, operationKey });
    const scope = this.paymentScope();
    const response = retry ? this.intents.retry(scope, intent, send) : this.intents.send(scope, intent, send);
    response.subscribe({
      next: () => {
        if (retry) this.pendingNotice.set('financialOperations.confirmed');
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
            : this.accountFeedback(error, 'customers.account.paymentFailed'),
        );
        this.submittingPayment.set(false);
        this.refreshSettlementPreview();
      },
    });
  }

  onStartReversal(receipt: PaymentReceiptSummary): void {
    this.reversalError.set('');
    this.reversalLocalizedError.set(undefined);
    this.reversalReason.set('');
    this.reversingReceiptId.set(receipt.receiptId);
  }

  onCancelReversal(): void {
    this.reversingReceiptId.set(null);
    this.reversalReason.set('');
    this.reversalError.set('');
    this.reversalLocalizedError.set(undefined);
  }

  onConfirmReversal(): void {
    const customer = this.selectedAccountCustomer();
    const receiptId = this.reversingReceiptId();
    const reason = this.reversalReason().trim();
    if (!customer || !receiptId || this.lockedReversalIds().includes(receiptId)) {
      return;
    }
    if (reason.length < 3) {
      this.reversalError.set('customers.account.reversalReasonRequired');
      return;
    }

    const operationKey = `reverse-${crypto.randomUUID()}`;
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
          this.submittingReversal.set(false);
          this.onReversalRecovered();
        },
        error: (error) => {
          this.submittingReversal.set(false);
          this.reversalError.set(
            classifyFinancialOperationFailure(error) === 'uncertain'
              ? 'financialOperations.outcomeUnknown'
              : this.accountFeedback(error, 'customers.account.reversalFailed', true),
          );
        },
      });
  }

  onReversalRecovered(): void {
    this.onCancelReversal();
    const customer = this.selectedAccountCustomer();
    if (customer) this.loadCustomerAccount(customer.customerId);
    this.customersFacade.loadCustomers();
  }

  tenderLabelKey(method: ReceiptTenderMethod): string {
    return `customers.account.tender.${method}`;
  }

  onStatusFilterChange(statusFilter: CustomerStatusFilter): void {
    this.statusFilter.set(statusFilter);
    clearTimeout(this.directorySearchTimer);
    this.loadDirectoryPage(1);
  }

  onAccountLedgerFilterChange(filter: AccountLedgerFilter): void {
    this.accountLedgerFilter.set(filter);
  }

  private loadCustomerAccount(customerId: string): void {
    this.accountError.set('');
    this.accountLocalizedError.set(undefined);
    this.accountLoading.set(true);

    this.refreshSettlementPreview();
    this.customerService.getCustomerAccount(customerId).subscribe({
      next: (account) => {
        this.selectedAccount.set(account);
        this.accountLoading.set(false);
      },
      error: (error) => {
        this.accountError.set(this.accountFeedback(error, 'customers.account.loadFailed'));
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
  accountFieldError(field: string) {
    return this.accountLocalizedError()?.fieldErrors.find((error) => error.field === field);
  }

  private accountFeedback(error: unknown, fallbackKey: string, reversal = false): string {
    const localized = this.apiErrors.resolve(error, {
      fallbackKey,
      codeMap: SETTLEMENT_ERROR_KEYS,
      fieldMap: {
        Amount: 'amount',
        PaymentDate: 'paymentDate',
        Method: 'method',
        ExternalReference: 'externalReference',
        Notes: 'notes',
      },
    });
    (reversal ? this.reversalLocalizedError : this.accountLocalizedError).set(localized);
    return localized.messageKey;
  }
}
