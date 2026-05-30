import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../../environments/environment';
import { AddCustomerRequest, CustomerService, EditCustomerRequest } from './customer.service';

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
    const payload: AddCustomerRequest = { name: 'Alice', phoneNumber: '+9198', address: null, isActive: true, creditLimit: 0 };
    const customer = { customerId: 'c1', ...payload, purchaseCount: 0, lifetimeRevenue: 0, currentMonthRevenue: 0 };
    service.addCustomer(payload).subscribe((res) => expect(res).toEqual(customer));

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(customer);
  });

  it('editCustomer performs PUT to /customers/:id', () => {
    const payload: EditCustomerRequest = { name: 'Alice Updated', phoneNumber: '+9198', address: 'Addr', isActive: true, creditLimit: 100 };
    const customerId = 'c1';
    const updated = { customerId, ...payload, purchaseCount: 1, lifetimeRevenue: 1200, currentMonthRevenue: 100 };
    service.editCustomer(customerId, payload).subscribe((res) => expect(res).toEqual(updated));

    const req = httpMock.expectOne(`${apiUrl}/${customerId}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush(updated);
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
});
