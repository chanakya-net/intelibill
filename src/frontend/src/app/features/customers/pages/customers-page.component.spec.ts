import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { Customer, CustomerService } from '../services/customer.service';
import { CustomersFacade } from '../state/customers.facade';
import { CustomersPageComponent } from './customers-page.component';

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
    getCustomerAccount: vi.fn(),
    recordCustomerPayment: vi.fn(),
  };

  let fixture: ComponentFixture<CustomersPageComponent>;
  let component: CustomersPageComponent;

  beforeEach(async () => {
    facade.loadCustomers.mockClear();
    facade.clearError.mockClear();
    facade.clearMutationStatus.mockClear();

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
                usage: 'Usage',
                newTransaction: 'New Transaction',
                creditLimit: 'Credit Limit',
                monthlyRevenue: 'Monthly Revenue',
                noCustomersFound: 'No customers found',
                noCustomersDescription: 'Start by adding your first customer.',
                summary: {
                  totalCustomers: 'Total Customers',
                  outstandingBalance: 'Outstanding Balance',
                  overdueCount: 'Overdue Accounts',
                  totalCreditIssued: 'Total Credit Issued',
                  accountsWithCredit: 'Accounts With Credit',
                  monthlyRevenue: 'Monthly Revenue',
                  filteredRows: 'Filtered Rows',
                },
                statuses: {
                  active: 'Active',
                  inactive: 'Inactive',
                  overdue: 'Overdue',
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
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersPageComponent);
    component = fixture.componentInstance;
  });

  it('computes summary metrics from customer rows', () => {
    expect(facade.loadCustomers).toHaveBeenCalled();
    expect(component.totalCustomers()).toBe(3);
    expect(component.outstandingBalance()).toBe(120);
    expect(component.overdueCount()).toBe(1);
    expect(component.totalCreditIssued()).toBe(700);
    expect(component.accountsWithCredit()).toBe(2);
    expect(component.monthlyRevenue()).toBe(680);
  });

  it('filters by name, phone, and address without using numeric metrics', () => {
    component.searchValue.set('warehouse');
    expect(component.filteredCustomers().map((customer) => customer.customerId)).toEqual(['c2']);

    component.searchValue.set('8888');
    expect(component.filteredCustomers().map((customer) => customer.customerId)).toEqual(['c2']);

    component.searchValue.set('120');
    expect(component.filteredCustomers().map((customer) => customer.customerId)).toEqual([]);
  });

  it('derives customer status and usage from activity and outstanding due', () => {
    const customers = customersSignal();

    expect(component.customerStatus(customers[0])).toBe('overdue');
    expect(component.customerStatus(customers[1])).toBe('inCredit');
    expect(component.customerStatus(customers[2])).toBe('inactive');
    expect(component.customerUsagePercent(customers[0])).toBe(24);
    expect(component.customerUsagePercent(customers[1])).toBe(0);
    expect(component.customerUsagePercent(customers[2])).toBe(0);
  });
});
