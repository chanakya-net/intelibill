import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, afterEach } from 'vitest';

import { SupplierInfoCardComponent } from './supplier-info-card.component';
import { Supplier } from '../../services/supplier.service';

describe('SupplierInfoCardComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders supplier name', () => {
    TestBed.configureTestingModule({
      imports: [
        SupplierInfoCardComponent,
        TranslocoTestingModule.forRoot({
          preloadLangs: true,
          langs: {
            'en-IN': {
              common: { active: 'Active', inactive: 'Inactive' },
              suppliers: {
                preferred: 'Preferred',
                contactPerson: 'Contact Person',
                contactPhone: 'Contact Phone',
                city: 'City',
                state: 'State',
                pin: 'PIN',
                address: 'Address',
              },
            },
          },
        }),
      ],
    });

    const fixture = TestBed.createComponent(SupplierInfoCardComponent);
    fixture.componentRef.setInput('supplier', {
      supplierId: 's1',
      name: 'Fresh Foods',
      contactPersonName: 'Ramesh',
      contactPersonPhone: '+919999999999',
      gstNumber: '27AAPFU0939F1ZV',
      address: '42 MG Road',
      city: 'Bengaluru',
      state: 'Karnataka',
      pin: '560001',
      isSystem: false,
      isActive: true,
      isPreferred: false,
      balanceDue: 0,
    } satisfies Supplier);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Fresh Foods');
    expect(fixture.nativeElement.textContent).toContain('27AAPFU0939F1ZV');
  });
});
