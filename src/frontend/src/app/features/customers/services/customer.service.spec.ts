import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../../environments/environment';
import {
  AddCustomerRequest,
  CustomerAccountSale,
  CustomerService,
  EditCustomerRequest,
  customerLedgerEntryKind,
  isCustomerDebit,
  suggestOldestFirstAllocations,
} from './customer.service';

describe('CustomerService', () => {
  let service: CustomerService;
  let httpMock: HttpTestingController;
  const apiUrl = `${environment.apiBaseUrl}/customers`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CustomerService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getCustomers performs GET to /customers', () => {
    const customers = [
      {
        customerId: 'c1',
        name: 'Alice',
        phoneNumber: '+9198',
        address: null,
        isActive: true,
        creditLimit: 0,
        purchaseCount: 0,
        lifetimeRevenue: 0,
        currentMonthRevenue: 0,
      },
    ];
    service.getCustomers().subscribe((res) => expect(res).toEqual(customers));

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.method).toBe('GET');
    req.flush(customers);
  });

  it('addCustomer performs POST to /customers', () => {
    const payload: AddCustomerRequest = { name: 'Alice', phoneNumber: '+9198', address: null, isActive: true, creditLimit: 0, gstin: null, stateCode: null };
    const customer = { customerId: 'c1', ...payload, purchaseCount: 0, lifetimeRevenue: 0, currentMonthRevenue: 0 };
    service.addCustomer(payload).subscribe((res) => expect(res).toEqual(customer));

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(customer);
  });

  it('editCustomer performs PUT to /customers/:id', () => {
    const payload: EditCustomerRequest = { name: 'Alice Updated', phoneNumber: '+9198', address: 'Addr', isActive: true, creditLimit: 100, gstin: null, stateCode: null };
    const customerId = 'c1';
    const updated = { customerId, ...payload, purchaseCount: 1, lifetimeRevenue: 1200, currentMonthRevenue: 100 };
    service.editCustomer(customerId, payload).subscribe((res) => expect(res).toEqual(updated));

    const req = httpMock.expectOne(`${apiUrl}/${customerId}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush(updated);
  });

  it('sends and receives the customer GSTIN and state', () => {
    const payload: AddCustomerRequest = {
      name: 'Bharat Stores',
      phoneNumber: '+919811111111',
      address: '5 Market Lane',
      isActive: true,
      creditLimit: 0,
      gstin: '29AAGCB7383J1Z4',
      stateCode: '29',
    };
    const customer = { customerId: 'c2', ...payload, purchaseCount: 0, lifetimeRevenue: 0, currentMonthRevenue: 0 };
    service.addCustomer(payload).subscribe((res) => {
      expect(res.gstin).toBe('29AAGCB7383J1Z4');
      expect(res.stateCode).toBe('29');
    });

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.body).toEqual(payload);
    req.flush(customer);
  });

  it('getCustomerAccount performs GET to /customers/:id/account', () => {
    const customerId = 'c1';
    const account = {
      customerId,
      name: 'Alice',
      phoneNumber: '+9198',
      outstandingDue: 120,
      sales: [],
      ledgerEntries: [],
      paymentHistory: [],
    };

    service.getCustomerAccount(customerId).subscribe((res) => expect(res).toEqual(account));

    const req = httpMock.expectOne(`${apiUrl}/${customerId}/account`);
    expect(req.request.method).toBe('GET');
    req.flush(account);
  });

  it('recordCustomerPayment performs POST to /customers/:id/payments', () => {
    const customerId = 'c1';
    const payload = {
      amount: 200,
      paymentDate: '2026-04-24',
      notes: 'Paid by cash',
      operationKey: 'customer-payment-1',
    };
    const ledgerEntry = {
      entryId: 'e1',
      saleId: null,
      entryType: 2,
      amount: 200,
      entryDate: '2026-04-24',
      notes: 'Paid by cash',
      runningBalance: 50,
    };

    service.recordCustomerPayment(customerId, payload).subscribe((res) => expect(res).toEqual(ledgerEntry));

    const req = httpMock.expectOne(`${apiUrl}/${customerId}/payments`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(ledgerEntry);
  });

  it('recordCustomerPayment sends the receipt method, reference and allocation targets', () => {
    const payload = {
      amount: 500,
      paymentDate: '2026-11-06',
      notes: null,
      method: 'Upi' as const,
      externalReference: 'UPI-4471',
      allocations: [
        { targetId: 's-old', amount: 300 },
        { targetId: 's-new', amount: 150 },
      ],
      operationKey: 'pay-1',
    };
    service.recordCustomerPayment('c1', payload).subscribe();

    const req = httpMock.expectOne(`${apiUrl}/c1/payments`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({ entryId: 'e1', saleId: null, entryType: 'PaymentReceived', amount: 500, entryDate: '2026-11-06', notes: null, runningBalance: 0, receiptId: 'r1' });
  });

  it('reversePayment posts the reason and operation key to /payments/:id/reversal', () => {
    service.reversePayment('r1', { reason: 'Wrong customer', operationKey: 'rev-1' }).subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/payments/r1/reversal`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'Wrong customer', operationKey: 'rev-1' });
    req.flush({});
  });

  it('getPaymentReceipt performs GET to /payments/:id', () => {
    service.getPaymentReceipt('r1').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/payments/r1`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });
});

describe('suggestOldestFirstAllocations', () => {
  const sale = (saleId: string, soldAt: string, outstanding: number): CustomerAccountSale => ({
    saleId,
    invoiceNumber: saleId,
    paymentMethod: 'Credit',
    soldAt,
    paidAmount: 0,
    dueAmount: outstanding,
    totalAmount: outstanding,
    currentOutstandingAmount: outstanding,
  });

  it('settles the oldest open invoices first and leaves the rest unallocated', () => {
    const sales = [sale('new', '2026-11-03T00:00:00Z', 100), sale('old', '2026-11-01T00:00:00Z', 70.5), sale('paid', '2026-10-01T00:00:00Z', 0)];

    expect(suggestOldestFirstAllocations(sales, 120.25)).toEqual([
      { targetId: 'old', amount: 70.5 },
      { targetId: 'new', amount: 49.75 },
    ]);
    expect(suggestOldestFirstAllocations(sales, 500)).toEqual([
      { targetId: 'old', amount: 70.5 },
      { targetId: 'new', amount: 100 },
    ]);
    expect(suggestOldestFirstAllocations(sales, 0)).toEqual([]);
  });
});

describe('customer ledger entry kinds', () => {
  it('reads enum names and legacy numbers, and treats a payment reversal as a debit', () => {
    expect(customerLedgerEntryKind('PaymentReversal')).toBe('PaymentReversal');
    expect(customerLedgerEntryKind(2)).toBe('PaymentReceived');
    expect(isCustomerDebit('PaymentReversal')).toBe(true);
    expect(isCustomerDebit('SaleDue')).toBe(true);
    expect(isCustomerDebit('PaymentReceived')).toBe(false);
    expect(isCustomerDebit('ReturnCredit')).toBe(false);
  });
  it('treats cancellation credit as a distinct credit in all wire forms', () => {
    for (const value of [6, 'SaleCancellationCredit', 'SALE_CANCELLATION_CREDIT']) {
      expect(customerLedgerEntryKind(value)).toBe('SaleCancellationCredit');
      expect(isCustomerDebit(value)).toBe(false);
    }
  });
});
