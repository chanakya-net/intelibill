import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';

export interface Customer {
  customerId: string;
  name: string;
  phoneNumber: string;
  address: string | null;
  isActive: boolean;
  outstandingDue?: number;
}

export interface AddCustomerRequest {
  name: string;
  phoneNumber: string;
  address: string | null;
  isActive: boolean;
}

export interface EditCustomerRequest {
  name: string;
  phoneNumber: string;
  address: string | null;
  isActive: boolean;
}

export interface CustomerAccountSale {
  saleId: string;
  invoiceNumber: string;
  paymentMethod: number;
  soldAt: string;
  paidAmount: number;
  dueAmount: number;
  totalAmount: number;
}

export interface CustomerLedgerEntry {
  entryId: string;
  saleId: string | null;
  entryType: number;
  amount: number;
  entryDate: string;
  notes: string | null;
  runningBalance: number;
}

export interface CustomerAccount {
  customerId: string;
  name: string;
  phoneNumber: string;
  outstandingDue: number;
  sales: CustomerAccountSale[];
  ledgerEntries: CustomerLedgerEntry[];
  paymentHistory: CustomerLedgerEntry[];
}

export interface RecordCustomerPaymentRequest {
  amount: number;
  paymentDate: string;
  notes: string | null;
}

@Injectable({
  providedIn: 'root',
})
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiBaseUrl}/customers`;

  getCustomers(): Observable<Customer[]> {
    return this.http.get<Customer[]>(this.apiUrl);
  }

  addCustomer(request: AddCustomerRequest): Observable<Customer> {
    return this.http.post<Customer>(this.apiUrl, request);
  }

  editCustomer(customerId: string, request: EditCustomerRequest): Observable<Customer> {
    return this.http.put<Customer>(`${this.apiUrl}/${customerId}`, request);
  }

  getCustomerAccount(customerId: string): Observable<CustomerAccount> {
    return this.http.get<CustomerAccount>(`${this.apiUrl}/${customerId}/account`);
  }

  recordCustomerPayment(customerId: string, request: RecordCustomerPaymentRequest): Observable<CustomerLedgerEntry> {
    return this.http.post<CustomerLedgerEntry>(`${this.apiUrl}/${customerId}/payments`, request);
  }
}
