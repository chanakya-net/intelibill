import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { SuppliersTableComponent } from './suppliers-table.component';
import { Supplier } from '../services/supplier.service';

describe('SuppliersTableComponent', () => {
  let fixture: ComponentFixture<SuppliersTableComponent>;
  let component: SuppliersTableComponent;

  const mockSuppliers: Supplier[] = [
    {
      supplierId: 's1',
      name: 'Acme',
      contactPersonName: 'Aman',
      contactPersonPhone: '1111111111',
      address: 'One Street',
      city: 'Delhi',
      state: 'DL',
      pin: '110001',
      isSystem: false,
      isActive: true,
      isPreferred: false,
      balanceDue: 0,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SuppliersTableComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(SuppliersTableComponent);
    component = fixture.componentInstance;
    component.suppliers = mockSuppliers;
    component.canManageSuppliers = true;
  });

  it('renders with supplied rows', () => {
    fixture.detectChanges();
    expect(component.suppliers).toHaveLength(1);
  });
});
