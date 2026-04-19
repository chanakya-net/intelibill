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
    const customers = [{ customerId: 'c1', name: 'Alice', phoneNumber: '+9198', address: null, isActive: true }];
    service.getCustomers().subscribe((res) => expect(res).toEqual(customers));

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.method).toBe('GET');
    req.flush(customers);
  });

  it('addCustomer performs POST to /customers', () => {
    const payload: AddCustomerRequest = { name: 'Alice', phoneNumber: '+9198', address: null, isActive: true };
    const customer = { customerId: 'c1', ...payload };
    service.addCustomer(payload).subscribe((res) => expect(res).toEqual(customer));

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(customer);
  });

  it('editCustomer performs PUT to /customers/:id', () => {
    const payload: EditCustomerRequest = { name: 'Alice Updated', phoneNumber: '+9198', address: 'Addr', isActive: true };
    const customerId = 'c1';
    const updated = { customerId, ...payload };
    service.editCustomer(customerId, payload).subscribe((res) => expect(res).toEqual(updated));

    const req = httpMock.expectOne(`${apiUrl}/${customerId}`);
    expect(req.request.method).toBe('PUT');
    req.flush(updated);
  });
});
