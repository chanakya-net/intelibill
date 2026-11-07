import '@angular/compiler';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
import { Customer, CustomerAccount, CustomerService } from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';
import { CustomersPageComponent } from './customers-page.component';

const paymentIntents = {
  revision: signal(0),
  pending: vi.fn((): unknown => null),
  check: vi.fn(),
  prepare: vi.fn((_scope: unknown, type: string, targetId: string, payload: unknown) => ({ type, targetId, payload, operationKey: 'op-1' })),
  send: vi.fn((_scope: unknown, intent: { payload: unknown; operationKey: string }, request: (payload: unknown, key: string) => unknown) =>
    request(intent.payload, intent.operationKey),
  ),
};

let shopRole = 'Manager';

const account: CustomerAccount = {
  customerId: 'c1',
  name: 'Alice Cooper',
  phoneNumber: '9999999999',
  outstandingDue: 150,
  unallocatedCredit: 20,
  sales: [
    { saleId: 's-new', invoiceNumber: 'A/2627/000002', paymentMethod: 'Credit', soldAt: '2026-11-03T10:00:00Z', paidAmount: 0, dueAmount: 100, totalAmount: 100, allocatedPaymentAmount: 0, returnCreditAmount: 0, currentOutstandingAmount: 100 },
    { saleId: 's-old', invoiceNumber: 'A/2627/000001', paymentMethod: 'Credit', soldAt: '2026-11-01T10:00:00Z', paidAmount: 0, dueAmount: 120, totalAmount: 120, allocatedPaymentAmount: 50, returnCreditAmount: 0, currentOutstandingAmount: 70 },
  ],
  ledgerEntries: [
    { entryId: 'e3', saleId: null, entryType: 'PaymentReversal', amount: 30, entryDate: '2026-11-05', notes: null, runningBalance: 150 },
    { entryId: 'e2', saleId: null, entryType: 'PaymentReceived', amount: 30, entryDate: '2026-11-04', notes: null, runningBalance: 120, receiptId: 'r1' },
  ],
  paymentHistory: [],
  receipts: [
    { receiptId: 'r1', amount: 30, method: 'Upi', externalReference: 'UPI-1', businessDate: '2026-11-04', isLegacy: false, isReversed: false, allocatedAmount: 10, unallocatedAmount: 20, ledgerEntryId: 'e2' },
  ],
};

describe('CustomersPageComponent', () => {
  const customersSignal = signal<Customer[]>([
    {
      customerId: 'c1',
      name: 'Alice Cooper',
      phoneNumber: '9999999999',
      address: 'Main Street',
      isActive: true,
      creditLimit: 500,
      purchaseCount: 4,
      lifetimeRevenue: 2400,
      currentMonthRevenue: 600,
      outstandingDue: 120,
    },
    {
      customerId: 'c2',
      name: 'Beta Store',
      phoneNumber: '8888888888',
      address: 'Warehouse Road',
      isActive: true,
      creditLimit: 200,
      purchaseCount: 2,
      lifetimeRevenue: 800,
      currentMonthRevenue: 80,
      outstandingDue: -40,
    },
    {
      customerId: 'c3',
      name: 'Gamma Corner',
      phoneNumber: '7777777777',
      address: 'Old Town',
      isActive: false,
      creditLimit: 0,
      purchaseCount: 1,
      lifetimeRevenue: 100,
      currentMonthRevenue: 0,
      outstandingDue: 0,
    },
  ]);

  const facade = {
    allCustomers: customersSignal,
    loadingCustomers: signal(false),
    submitting: signal(false),
    errorMessage: signal(''),
    lastMutationType: signal<'add-customer' | 'edit-customer' | null>(null),
    lastMutationSucceeded: signal(false),
    loadCustomers: vi.fn(),
    addCustomer: vi.fn(),
    editCustomer: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
  };

  const customerService = {
    getCustomerAccount: vi.fn(() => of(account)),
    recordCustomerPayment: vi.fn(() => of({})),
    reversePayment: vi.fn(() => of({})),
  };
  const router = {
    navigate: vi.fn().mockResolvedValue(true),
  };

  let fixture: ComponentFixture<CustomersPageComponent>;
  let component: CustomersPageComponent;

  beforeEach(async () => {
    facade.loadCustomers.mockClear();
    facade.clearError.mockClear();
    facade.clearMutationStatus.mockClear();
    router.navigate.mockClear();

    await TestBed.configureTestingModule({
      imports: [
        CustomersPageComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            'en-IN': {
              common: {
                currencySymbol: '₹',
                actions: 'Actions',
                all: 'All',
              },
              customers: {
                customerDirectory: 'Customer Directory',
                title: 'Customers',
                subtitle: 'Customers added by you are available across all your shops.',
                addCustomer: 'Add Customer',
                searchPlaceholder: 'Search customers by name, phone, or address...',
                showingCount: 'Showing {{visible}} of {{total}} customers',
                active: 'Active',
                inactive: 'Inactive',
                overdue: 'Overdue',
                inCredit: 'In Credit',
                status: 'Status',
                usage: 'Usage',
                newTransaction: 'New Transaction',
                creditLimit: 'Credit Limit',
                monthlyRevenue: 'Monthly Revenue',
                noCustomersFound: 'No customers found',
                noCustomersDescription: 'Start by adding your first customer.',
                summary: {
                  totalCustomers: 'Total Customers',
                  outstandingBalance: 'Outstanding Balance',
                  outstandingCount: 'Accounts With Outstanding',
                  totalCreditIssued: 'Total Credit Issued',
                  accountsWithCredit: 'Accounts With Credit',
                  monthlyRevenue: 'Monthly Revenue',
                  filteredRows: 'Filtered Rows',
                },
                statuses: {
                  active: 'Active',
                  inactive: 'Inactive',
                  outstanding: 'Outstanding',
                  inCredit: 'In Credit',
                },
                account: {
                  recordPayment: 'Record Payment',
                  outstandingDue: 'Outstanding Due',
                  submitPayment: 'Submit Payment',
                  amount: 'Amount',
                  paymentDate: 'Payment Date',
                  notes: 'Notes',
                  ledger: 'Ledger',
                  date: 'Date',
                  type: 'Type',
                  paymentReceived: 'Payment Received',
                  saleDue: 'Sale Due',
                  loadFailed: 'Unable to load customer account right now.',
                  paymentFailed: 'Unable to record payment right now.',
                },
              },
            },
          },
          preloadLangs: true,
          translocoConfig: {
            availableLangs: ['en-IN'],
            defaultLang: 'en-IN',
            reRenderOnLangChange: true,
          },
        }),
      ],
      providers: [
        { provide: CustomersFacade, useValue: facade },
        { provide: CustomerService, useValue: customerService },
        { provide: Router, useValue: router },
        {
          provide: AuthService,
          useValue: {
            session: () => ({
              user: { id: 'user-1' },
              activeShopId: 'shop-1',
              shops: [{ shopId: 'shop-1', shopName: 'Main', role: shopRole, isDefault: true, lastUsedAt: null }],
            }),
          },
        },
        { provide: FinancialOperationIntentService, useValue: paymentIntents },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersPageComponent);
    component = fixture.componentInstance;
  });

  it('computes summary metrics from customer rows', () => {
    expect(facade.loadCustomers).toHaveBeenCalled();
    expect(component.totalCustomers()).toBe(3);
    expect(component.outstandingBalance()).toBe(120);
    // Owing something is "outstanding"; overdue needs a due date that has passed.
    expect(component.outstandingCount()).toBe(1);
    expect(component.totalCreditIssued()).toBe(700);
    expect(component.accountsWithCredit()).toBe(2);
    expect(component.monthlyRevenue()).toBe(680);
  });

  it('filters by name, phone, address, and reference (customerId) without using numeric metrics', () => {
    component.searchValue.set('warehouse');
    expect(component.filteredCustomers().map((customer) => customer.customerId)).toEqual(['c2']);

    component.searchValue.set('8888');
    expect(component.filteredCustomers().map((customer) => customer.customerId)).toEqual(['c2']);

    component.searchValue.set('c1');
    expect(component.filteredCustomers().map((customer) => customer.customerId)).toEqual(['c1']);

    component.searchValue.set('120');
    expect(component.filteredCustomers().map((customer) => customer.customerId)).toEqual([]);
  });

  it('derives customer status and usage from activity and outstanding due', () => {
    const customers = customersSignal();

    expect(component.customerStatus(customers[0])).toBe('outstanding');
    expect(component.customerStatus(customers[1])).toBe('inCredit');
    expect(component.customerStatus(customers[2])).toBe('inactive');
    expect(component.customerUsagePercent(customers[0])).toBe(24);
    expect(component.customerUsagePercent(customers[1])).toBe(0);
    expect(component.customerUsagePercent(customers[2])).toBe(0);
  });

  it('navigates to the new sale route with the selected customerId', () => {
    component.onOpenNewTransaction(customersSignal()[0]);

    expect(router.navigate).toHaveBeenCalledWith(['/sales/new'], {
      queryParams: { customerId: 'c1' },
    });
  });

  it('sends the oldest-first allocation only when the operator accepts it', () => {
    component.onOpenCustomerAccount(customersSignal()[0]);
    component.paymentForm.patchValue({ amount: 100, method: 'Upi', externalReference: ' UPI-99 ' });

    expect(component.allocationSuggestion().rows).toEqual([
      { targetId: 's-old', amount: 70, invoiceNumber: 'A/2627/000001' },
      { targetId: 's-new', amount: 30, invoiceNumber: 'A/2627/000002' },
    ]);

    component.onRecordPayment();
    expect(customerService.recordCustomerPayment).toHaveBeenLastCalledWith('c1', expect.objectContaining({
      amount: 100,
      method: 'Upi',
      externalReference: 'UPI-99',
      allocations: [],
      operationKey: 'op-1',
    }));

    component.paymentForm.patchValue({ amount: 100, applyOldestFirst: true });
    component.onRecordPayment();
    expect(customerService.recordCustomerPayment).toHaveBeenLastCalledWith('c1', expect.objectContaining({
      allocations: [
        { targetId: 's-old', amount: 70 },
        { targetId: 's-new', amount: 30 },
      ],
    }));
  });

  it('shows current outstanding separately and books a payment reversal as a debit', () => {
    component.onOpenCustomerAccount(customersSignal()[0]);

    expect(component.selectedAccountOpenInvoices().map((sale) => sale.saleId)).toEqual(['s-old', 's-new']);
    expect(component.selectedAccountUnallocatedCredit()).toBe(20);
    const reversal = component.selectedAccountLedgerRows().find((row) => row.entry.entryId === 'e3')!;
    expect(reversal.debit).toBe(30);
    expect(reversal.credit).toBeNull();
    expect(reversal.labelKey).toBe('customers.account.paymentReversal');
  });

  it('lets only an owner reverse a receipt, with a reason and a stable operation key', () => {
    expect(component.isOwner()).toBe(false);
    shopRole = 'Owner';
    try {
      const ownerFixture = TestBed.createComponent(CustomersPageComponent);
      const owner = ownerFixture.componentInstance;
      owner.onOpenCustomerAccount(customersSignal()[0]);
      expect(owner.isOwner()).toBe(true);

      owner.onStartReversal(account.receipts![0]);
      owner.reversalReason.set('x');
      owner.onConfirmReversal();
      expect(owner.reversalError()).toBe('customers.account.reversalReasonRequired');
      expect(customerService.reversePayment).not.toHaveBeenCalled();

      owner.reversalReason.set('Wrong customer');
      owner.onConfirmReversal();
      expect(customerService.reversePayment).toHaveBeenCalledWith('r1', {
        reason: 'Wrong customer',
        operationKey: expect.stringMatching(/^reverse-/),
      });
      expect(owner.reversingReceiptId()).toBeNull();
    } finally {
      shopRole = 'Manager';
    }
  });

  it('shows the saved credit limit, never lifetime revenue (RF1)', () => {
    customerService.getCustomerAccount.mockReturnValueOnce(
      of({
        ...account,
        outstandingDue: 500,
        creditLimit: 2000,
        creditPolicyMode: 'Enforced' as const,
        availableCredit: 1500,
        sales: [{ ...account.sales[0], totalAmount: 20000 }],
      }),
    );
    component.onOpenCustomerAccount(customersSignal()[0]);

    expect(component.selectedAccountLifetimeRevenue()).toBe(20000);
    expect(component.selectedAccountCreditLimit()).toBe(2000);
    expect(component.selectedAccountUsagePercent()).toBe(25);
    expect(component.selectedAccountAvailableCredit()).toBe(1500);
    expect(component.selectedAccountCreditPolicyLabelKey()).toBe('customers.creditPolicy.modes.Enforced');
  });

  it('labels a response without a policy as legacy advisory with no promised credit', () => {
    component.onOpenCustomerAccount(customersSignal()[0]);

    expect(component.selectedAccountCreditPolicy()).toBe('LegacyAdvisory');
    expect(component.selectedAccountAvailableCredit()).toBeNull();
  });

  it('uses the server opening and closing balances, not a displayed row (RF5)', () => {
    customerService.getCustomerAccount.mockReturnValueOnce(of({ ...account, openingBalance: 0, closingBalance: 150 }));
    component.onOpenCustomerAccount(customersSignal()[0]);

    // The oldest displayed row's after-entry balance is 120; the opening before the window is 0.
    expect(component.selectedAccountOpeningBalance()).toBe(0);
    expect(component.selectedAccountClosingBalance()).toBe(150);
  });

  it('marks only past-due invoices overdue and shows the inactive state (RF2)', () => {
    customerService.getCustomerAccount.mockReturnValueOnce(
      of({
        ...account,
        isActive: false,
        overdueAmount: 70,
        sales: [
          { ...account.sales[0], dueDate: null, isOverdue: false },
          { ...account.sales[1], dueDate: '2026-11-02', isOverdue: true },
        ],
      }),
    );
    component.onOpenCustomerAccount(customersSignal()[0]);
    fixture.detectChanges();

    const statuses = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="invoice-due-status"] .statement-type'),
    ).map((badge) => badge.classList.contains('overdue'));
    expect(statuses).toEqual([true, false]);
    expect(component.selectedAccountOverdueAmount()).toBe(70);
    expect(component.selectedAccountIsActive()).toBe(false);
  });
 it('shows cancellation credit separately and never reopens the original issue due',()=>{customerService.getCustomerAccount.mockReturnValueOnce(of({...account,sales:[{...account.sales[0],status:'Cancelled',cancellationCreditAmount:100,currentOutstandingAmount:0}],ledgerEntries:[{entryId:'cancel-credit',saleId:'s-new',entryType:'SaleCancellationCredit',amount:100,entryDate:'2026-11-08',notes:'SC-frozen',runningBalance:0}]}));component.onOpenCustomerAccount(customersSignal()[0]);expect(component.selectedAccountOpenInvoices()).toEqual([]);const row=component.selectedAccountLedgerRows()[0];expect(row.labelKey).toBe('customers.account.saleCancellationCredit');expect(row.credit).toBe(100);expect(row.debit).toBeNull();});
});
