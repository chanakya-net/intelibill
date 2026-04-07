import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { Observable } from 'rxjs';

import { SUPPLIER_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface Supplier {
  readonly supplierId: string;
  readonly name: string;
  readonly contactPersonName: string | null;
  readonly contactPersonPhone: string | null;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pin: string;
  readonly isActive: boolean;
  readonly isPreferred: boolean;
  readonly balanceDue: number;
}

export interface AddSupplierRequest {
  readonly name: string;
  readonly contactPersonName: string | null;
  readonly contactPersonPhone: string | null;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pin: string;
  readonly isActive: boolean;
  readonly isPreferred: boolean;
}

export interface EditSupplierRequest {
  readonly name: string;
  readonly contactPersonName: string | null;
  readonly contactPersonPhone: string | null;
  readonly address: string;
  readonly city: string;
  readonly state: string;
  readonly pin: string;
  readonly isActive: boolean;
  readonly isPreferred: boolean;
}

@Injectable({ providedIn: 'root' })
export class SupplierService {
  private readonly http = inject(HttpClient);

  getSuppliers(): Observable<readonly Supplier[]> {
    return this.http.get<readonly Supplier[]>(SUPPLIER_ENDPOINTS.list);
  }

  addSupplier(payload: AddSupplierRequest): Observable<Supplier> {
    return this.http.post<Supplier>(SUPPLIER_ENDPOINTS.add, payload);
  }

  editSupplier(supplierId: string, payload: EditSupplierRequest): Observable<Supplier> {
    return this.http.put<Supplier>(SUPPLIER_ENDPOINTS.update(supplierId), payload);
  }
}
