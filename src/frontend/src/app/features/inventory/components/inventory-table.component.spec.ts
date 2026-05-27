import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

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
      unitPrice: 15,
      currentStockValue: 300,
      reorderLevel: 5,
      stockStatus: 'inStock',
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
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('Paracetamol');
    expect(host.textContent).toContain('123');
    expect(host.textContent).toContain('PCS');
    expect(host.textContent).toContain('en.inventory.barcodeOrSku');
    expect(host.textContent).toContain('en.inventory.unitPrice');
    expect(host.textContent).toContain('en.inventory.currentStock');
    expect(host.textContent).toContain('en.inventory.currentStockValue');
  });

  it('emits page 1 when page size changes', () => {
    const spy = vi.spyOn(component.pageChange, 'emit');
    component.onPageChange({ page: 3, rows: 25 });

    expect(spy).toHaveBeenCalledWith({ page: 1, rows: 25 });
  });

  it('emits navigated page when only page changes', () => {
    const spy = vi.spyOn(component.pageChange, 'emit');
    component.onPageChange({ page: 2, rows: 20 });

    expect(spy).toHaveBeenCalledWith({ page: 3, rows: 20 });
  });

  it('maps stock status to translation keys', () => {
    expect(component.stockStatusLabelKey('inStock')).toBe('inventory.inStock');
    expect(component.stockStatusLabelKey('runningLow')).toBe('inventory.reorder');
    expect(component.stockStatusLabelKey('critical')).toBe('inventory.outOfStock');
  });

  it('renders mobile paginator for server-side page navigation', () => {
    component.totalCount = 30;
    component.pageNumber = 1;
    component.pageSize = 10;
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const paginator = host.querySelector('.mobile-grid-container p-paginator');
    expect(paginator).not.toBeNull();
  });
});
