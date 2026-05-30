import { Customer } from '../services/customer.service';

export type CustomerStatus = 'active' | 'inactive' | 'overdue' | 'inCredit';

type CustomerStatusSource = Pick<Customer, 'isActive' | 'outstandingDue'>;
type CustomerUsageSource = Pick<Customer, 'creditLimit' | 'outstandingDue'>;

export function customerStatus(customer: CustomerStatusSource): CustomerStatus {
  if (!customer.isActive) {
    return 'inactive';
  }

  const outstandingDue = customer.outstandingDue ?? 0;

  if (outstandingDue > 0) {
    return 'overdue';
  }

  if (outstandingDue < 0) {
    return 'inCredit';
  }

  return 'active';
}

export function customerStatusLabelKey(customer: CustomerStatusSource): string {
  return `customers.statuses.${customerStatus(customer)}`;
}

export function customerStatusClass(customer: CustomerStatusSource): string {
  const status = customerStatus(customer);
  return `status-badge--${status === 'inCredit' ? 'in-credit' : status}`;
}

export function customerUsagePercent(customer: CustomerUsageSource): number {
  const creditLimit = customer.creditLimit ?? 0;

  if (creditLimit <= 0) {
    return 0;
  }

  const usage = (Math.max(0, customer.outstandingDue ?? 0) / creditLimit) * 100;
  return Math.min(100, Math.max(0, Math.round(usage)));
}

export function customerUsageLabel(customer: CustomerUsageSource): string {
  return `${customerUsagePercent(customer)}%`;
}
