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
}
