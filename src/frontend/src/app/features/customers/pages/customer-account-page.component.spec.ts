import { convertToParamMap, ActivatedRoute } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { CustomerAccountPageComponent } from './customer-account-page.component';
import { CustomerService } from '../services/customer.service';

describe('CustomerAccountPageComponent', () => {
  const accountResponse = {
    customerId: 'c1',
    name: 'Alice',
    phoneNumber: '+919000000001',
    outstandingDue: 120,
    sales: [],
    ledgerEntries: [],
    paymentHistory: [],
  };

  const customerService = {
    getCustomerAccount: vi.fn(),
    recordCustomerPayment: vi.fn(),
  };

  const createActivatedRoute = (customerId: string | null) => ({
    snapshot: {
      paramMap: convertToParamMap(customerId ? { customerId } : {}),
    },
  });

  beforeEach(() => {
    customerService.getCustomerAccount.mockReset();
    customerService.recordCustomerPayment.mockReset();
    customerService.getCustomerAccount.mockReturnValue(of(accountResponse));
    customerService.recordCustomerPayment.mockReturnValue(
      of({
        entryId: 'e1',
        saleId: null,
        entryType: 2,
        amount: 100,
        entryDate: '2026-04-24',
        notes: null,
        runningBalance: 20,
      })
    );
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads account on init when route has customer id', () => {
    TestBed.configureTestingModule({
      imports: [CustomerAccountPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: CustomerService, useValue: customerService },
        { provide: ActivatedRoute, useValue: createActivatedRoute('c1') },
      ],
    });

    const fixture = TestBed.createComponent(CustomerAccountPageComponent);
    const component = fixture.componentInstance;

    expect(customerService.getCustomerAccount).toHaveBeenCalledWith('c1');
    expect(component.hasAccount()).toBe(true);
    expect(component.errorMessage()).toBe('');
    expect(component.isLoading()).toBe(false);
  });

  it('sets notFound error when route has no customer id', () => {
    TestBed.configureTestingModule({
      imports: [CustomerAccountPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: CustomerService, useValue: customerService },
        { provide: ActivatedRoute, useValue: createActivatedRoute(null) },
      ],
    });

    const fixture = TestBed.createComponent(CustomerAccountPageComponent);
    const component = fixture.componentInstance;

    expect(customerService.getCustomerAccount).not.toHaveBeenCalled();
    expect(component.errorMessage()).toBe('customers.account.notFound');
  });

  it('does not call payment API when form is invalid', () => {
    TestBed.configureTestingModule({
      imports: [CustomerAccountPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: CustomerService, useValue: customerService },
        { provide: ActivatedRoute, useValue: createActivatedRoute('c1') },
      ],
    });

    const fixture = TestBed.createComponent(CustomerAccountPageComponent);
    const component = fixture.componentInstance;
    component.paymentForm.controls.amount.setValue(0);

    component.onRecordPayment();

    expect(customerService.recordCustomerPayment).not.toHaveBeenCalled();
  });

  it('records payment and reloads account on success', () => {
    TestBed.configureTestingModule({
      imports: [CustomerAccountPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: CustomerService, useValue: customerService },
        { provide: ActivatedRoute, useValue: createActivatedRoute('c1') },
      ],
    });

    const fixture = TestBed.createComponent(CustomerAccountPageComponent);
    const component = fixture.componentInstance;

    component.paymentForm.patchValue({ amount: 100, paymentDate: '2026-04-24', notes: '  paid  ' });
    component.onRecordPayment();

    expect(customerService.recordCustomerPayment).toHaveBeenCalledWith('c1', {
      amount: 100,
      paymentDate: '2026-04-24',
      notes: 'paid',
    });
    expect(customerService.getCustomerAccount).toHaveBeenCalledTimes(2);
  });

  it('sets payment error when API fails', () => {
    customerService.recordCustomerPayment.mockReturnValue(
      throwError(() => ({ error: { detail: 'Payment failed' } }))
    );

    TestBed.configureTestingModule({
      imports: [CustomerAccountPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: CustomerService, useValue: customerService },
        { provide: ActivatedRoute, useValue: createActivatedRoute('c1') },
      ],
    });

    const fixture = TestBed.createComponent(CustomerAccountPageComponent);
    const component = fixture.componentInstance;

    component.paymentForm.patchValue({ amount: 100, paymentDate: '2026-04-24', notes: '' });
    component.onRecordPayment();

    expect(component.errorMessage()).toBe('Payment failed');
    expect(component.isLoading()).toBe(false);
  });
});
