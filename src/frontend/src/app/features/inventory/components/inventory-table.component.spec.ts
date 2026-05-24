import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { InventoryTableComponent } from './inventory-table.component';
import { Item } from '../services/inventory.models';

describe('InventoryTableComponent', () => {
  let fixture: ComponentFixture<InventoryTableComponent>;
  let component: InventoryTableComponent;

  const mockItems: Item[] = [
    {
      id: 'i1',
      name: 'Paracetamol',
      barcode: '123',
      uom: 'PCS',
      isActive: true,
      currentStock: 20,
      description: null,
      hsnCode: null,
      defaultTaxRatePercent: 0,
      defaultTaxIncluded: false,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InventoryTableComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(InventoryTableComponent);
    component = fixture.componentInstance;
    component.items = mockItems;
  });

  it('renders rows from input data', () => {
    fixture.detectChanges();
    expect(component.items).toHaveLength(1);
  });
});
