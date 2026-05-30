import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { Customer } from '../services/customer.service';
import { CustomersTableComponent } from './customers-table.component';

describe('CustomersTableComponent', () => {
  let fixture: ComponentFixture<CustomersTableComponent>;
  let component: CustomersTableComponent;

  const mockCustomers: Customer[] = [
    {
      customerId: 'c1',
      name: 'Alice Cooper',
      phoneNumber: '9999999999',
      address: 'Street 1',
      isActive: true,
      creditLimit: 500,
      purchaseCount: 4,
      lifetimeRevenue: 2_400,
      currentMonthRevenue: 600,
      outstandingDue: 100,
    },
    {
      customerId: 'c2',
      name: 'Beta Store',
      phoneNumber: '8888888888',
      address: 'Market Road',
      isActive: true,
      creditLimit: 200,
      purchaseCount: 2,
      lifetimeRevenue: 800,
      currentMonthRevenue: 120,
      outstandingDue: -40,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        CustomersTableComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            'en-IN': {
              common: { actions: 'Actions' },
              customers: {
                name: 'Customer',
                phoneNumber: 'Phone',
                creditLimit: 'Credit Limit',
                status: 'Status',
                usage: 'Usage',
                overdue: 'Overdue',
                inCredit: 'In Credit',
                statuses: {
                  active: 'Active',
                  inactive: 'Inactive',
                  overdue: 'Overdue',
                  inCredit: 'In Credit',
                },
                newTransaction: 'New Transaction',
                editCustomer: 'Edit Customer',
                noCustomersFound: 'No customers found',
                noCustomersDescription: 'Start by adding your first customer.',
                showingCount: 'Showing {{visible}} of {{total}} customers',
                account: {
                  outstandingDue: 'Outstanding Due',
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
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersTableComponent);
    component = fixture.componentInstance;
    component.customers = mockCustomers;
    component.visibleRows = mockCustomers.length;
    component.totalRows = mockCustomers.length;
  });

  it('renders rows, status chips, and usage bars', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('Alice Cooper');
    expect(host.textContent).toContain('Overdue');
    expect(host.textContent).toContain('In Credit');

    const usageBars = host.querySelectorAll('.usage-bar__fill');
    expect(usageBars.length).toBeGreaterThanOrEqual(2);
    expect((usageBars[0] as HTMLElement).style.width).toBe('20%');
    expect((usageBars[1] as HTMLElement).style.width).toBe('0%');
  });

  it('emits account, transaction, and edit actions', () => {
    const accountSpy = vi.fn();
    const transactionSpy = vi.fn();
    const editSpy = vi.fn();

    component.openCustomerAccount.subscribe(accountSpy);
    component.newTransaction.subscribe(transactionSpy);
    component.openEditCustomer.subscribe(editSpy);

    fixture.detectChanges();

    const firstMobileCard = fixture.nativeElement.querySelector('.customer-card') as HTMLElement;
    firstMobileCard.querySelector('.customer-card__identity')?.dispatchEvent(new Event('click', { bubbles: true }));

    const buttons = Array.from(firstMobileCard.querySelectorAll('button'));
    buttons.find((button) => button.textContent?.includes('New Transaction'))?.click();
    buttons.find((button) => button.textContent?.includes('Edit Customer'))?.click();

    expect(accountSpy).toHaveBeenCalledWith(mockCustomers[0]);
    expect(transactionSpy).toHaveBeenCalledWith(mockCustomers[0]);
    expect(editSpy).toHaveBeenCalledWith(mockCustomers[0]);
  });
});
