import { HttpParams } from '@angular/common/http';
import type { Customer } from '../../features/customers/services/customer.service';
import type { Supplier } from '../../features/suppliers/services/supplier.service';

export type PartyStatus = 'all' | 'active' | 'inactive';
export interface PartyDirectoryQuery {
  search?: string;
  status?: PartyStatus;
  pageNumber?: number;
  pageSize?: number;
  includeSystem?: boolean;
}
export interface PartyDirectoryPage<T, S> {
  items: readonly T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  summary: S;
}
export interface CustomerDirectorySummary {
  totalCustomers: number;
  outstandingBalance: number;
  outstandingCount: number;
  totalCreditIssued: number;
  accountsWithCredit: number;
  monthlyRevenue: number;
}
export interface SupplierDirectorySummary {
  totalSuppliers: number;
  activeSuppliers: number;
  preferredSuppliers: number;
  pendingPayables: number;
}
export type CustomerDirectoryPage = PartyDirectoryPage<Customer, CustomerDirectorySummary>;
export type SupplierDirectoryPage = PartyDirectoryPage<Supplier, SupplierDirectorySummary>;
export const EMPTY_CUSTOMER_SUMMARY: CustomerDirectorySummary = {
  totalCustomers: 0,
  outstandingBalance: 0,
  outstandingCount: 0,
  totalCreditIssued: 0,
  accountsWithCredit: 0,
  monthlyRevenue: 0,
};
export const EMPTY_SUPPLIER_SUMMARY: SupplierDirectorySummary = {
  totalSuppliers: 0,
  activeSuppliers: 0,
  preferredSuppliers: 0,
  pendingPayables: 0,
};
export const DEFAULT_DIRECTORY_QUERY: PartyDirectoryQuery = {
  search: '',
  status: 'all',
  pageNumber: 1,
  pageSize: 20,
};
export function directoryParams(query: PartyDirectoryQuery, supplier = false): HttpParams {
  let params = new HttpParams()
    .set('search', query.search?.trim() ?? '')
    .set('status', query.status ?? 'all')
    .set('pageNumber', query.pageNumber ?? 1)
    .set('pageSize', query.pageSize ?? 20);
  if (supplier) params = params.set('include_system', query.includeSystem ?? true);
  return params;
}
export function customerOptionLabel(
  customer: Pick<Customer, 'customerId' | 'name' | 'phoneNumber'>,
): string {
  return `${customer.name} · ${customer.phoneNumber || customer.customerId} · ${customer.customerId}`;
}
export function supplierOptionLabel(
  supplier: Pick<Supplier, 'supplierId' | 'name' | 'city' | 'contactPersonPhone'>,
): string {
  return `${supplier.name} · ${supplier.contactPersonPhone || supplier.city || supplier.supplierId} · ${supplier.supplierId}`;
}
