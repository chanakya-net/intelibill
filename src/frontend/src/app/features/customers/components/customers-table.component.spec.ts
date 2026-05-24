import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { CustomersTableComponent } from './customers-table.component';
import { Customer } from '../services/customer.service';

describe('CustomersTableComponent', () => {
  let fixture: ComponentFixture<CustomersTableComponent>;
  let component: CustomersTableComponent;

  const mockCustomers: Customer[] = [
    {
      customerId: 'c1',
      name: 'Alice',
      phoneNumber: '9999999999',
      address: 'Street 1',
      isActive: true,
      outstandingDue: 200,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomersTableComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersTableComponent);
    component = fixture.componentInstance;
    component.customers = mockCustomers;
  });

  it('renders rows from input data', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('Alice');
    expect(host.textContent).toContain('9999999999');
  });
});
