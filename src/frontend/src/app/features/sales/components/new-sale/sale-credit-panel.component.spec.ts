import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { SimpleChange } from '@angular/core';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';

import { CustomerCreditStatus, CustomerService } from '../../../customers/services/customer.service';
import { customerCreditErrorKey } from '../../utils/customer-credit-error-keys';
import { SaleCreditPanelComponent } from './sale-credit-panel.component';

const enforced: CustomerCreditStatus = {
  customerId: 'c1',
  isActive: true,
  creditPolicyMode: 'Enforced',
  creditLimit: 100,
  creditTermsDays: null,
  currentExposure: 90,
  availableCredit: 10,
};

function setup(status: CustomerCreditStatus, canOverride: boolean) {
  const customerService = { getCustomerCredit: vi.fn(() => of(status)) };
  TestBed.configureTestingModule({
    imports: [SaleCreditPanelComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    providers: [{ provide: CustomerService, useValue: customerService }],
  });
  const fixture = TestBed.createComponent(SaleCreditPanelComponent);
  const component = fixture.componentInstance;
  component.customerId = status.customerId;
  component.dueAmount = 0;
  component.canOverride = canOverride;
  component.ngOnChanges({ customerId: new SimpleChange(null, status.customerId, true) });
  const emitted: unknown[] = [];
  component.overrideChange.subscribe((value) => emitted.push(value));
  return { fixture, component, customerService, emitted };
}

function setDue(component: SaleCreditPanelComponent, due: number): void {
  const previous = component.dueAmount;
  component.dueAmount = due;
  component.ngOnChanges({ dueAmount: new SimpleChange(previous, due, false) });
}

describe('SaleCreditPanelComponent', () => {
  it('previews the enforced limit and flags a due over it', () => {
    const { component, customerService } = setup(enforced, false);
    setDue(component, 10);
    expect(customerService.getCustomerCredit).toHaveBeenCalledWith('c1');
    expect(component.exceedsLimit()).toBe(false);

    setDue(component, 10.01);
    expect(component.exceedsLimit()).toBe(true);
  });

  it('lets an owner or manager approve an override with the balance they saw', () => {
    const { component, emitted } = setup(enforced, true);
    setDue(component, 50);

    component.onApprove();
    expect(emitted).toEqual([]);

    component.onReasonChange('  Festival order  ');
    component.onApprove();
    expect(emitted).toEqual([{ reason: 'Festival order', observedExposure: 90 }]);
    expect(component.approved()).toBe(true);
  });

  it('drops the approval when the due changes', () => {
    const { component, emitted } = setup(enforced, true);
    setDue(component, 50);
    component.onReasonChange('ok');
    component.onApprove();

    setDue(component, 60);

    expect(emitted.at(-1)).toBeNull();
    expect(component.approved()).toBe(false);
  });

  it('fetches the preview again and drops the approval after a credit refusal', () => {
    const { component, customerService, emitted } = setup(enforced, true);
    setDue(component, 50);
    component.onReasonChange('ok');
    component.onApprove();
    customerService.getCustomerCredit.mockClear();

    component.serverError = 'sales.newSale.credit.errors.overrideStale';
    component.ngOnChanges({ serverError: new SimpleChange('', component.serverError, false) });

    expect(customerService.getCustomerCredit).toHaveBeenCalledWith('c1');
    expect(emitted.at(-1)).toBeNull();
    expect(component.approved()).toBe(false);
  });

  it('never offers an override to staff', () => {
    const { component, emitted } = setup(enforced, false);
    setDue(component, 50);
    component.onReasonChange('ok');
    component.onApprove();
    expect(emitted).toEqual([]);
  });

  it('blocks due for no-credit and inactive customers', () => {
    const noCredit = setup({ ...enforced, creditPolicyMode: 'NoCredit', availableCredit: 0 }, true).component;
    setDue(noCredit, 1);
    expect(noCredit.isBlocked()).toBe(true);
    expect(noCredit.exceedsLimit()).toBe(false);
  });

  it('does not ask the server while offline', () => {
    const { component, customerService } = setup(enforced, true);
    customerService.getCustomerCredit.mockClear();
    component.isOfflineMode = true;
    component.ngOnChanges({ isOfflineMode: new SimpleChange(false, true, false) });
    expect(customerService.getCustomerCredit).not.toHaveBeenCalled();
    expect(component.status()).toBeNull();
  });
});

describe('customerCreditErrorKey', () => {
  it('maps credit refusals and ignores other codes', () => {
    expect(customerCreditErrorKey('Customer.CreditLimitExceeded')).toBe('sales.newSale.credit.errors.limitExceeded');
    expect(customerCreditErrorKey('Customer.CreditBusy')).toBe('sales.newSale.credit.errors.busy');
    expect(customerCreditErrorKey('Sale.Other')).toBeNull();
    expect(customerCreditErrorKey(undefined)).toBeNull();
  });
});
