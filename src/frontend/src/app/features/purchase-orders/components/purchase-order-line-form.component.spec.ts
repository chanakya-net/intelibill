import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { PurchaseOrderLineFormComponent } from './purchase-order-line-form.component';

describe('PurchaseOrderLineFormComponent', () => {
  const catalog = {
    filterByName: vi.fn(() => [{ itemId: 'item-1', name: 'Widget', barcode: 'W1' }]),
    findByName: vi.fn((name: string) => name === 'Widget' ? { itemId: 'item-1', name: 'Widget', barcode: 'W1' } : undefined),
    upsertEntry: vi.fn(),
  };
  const inventory = {
    generateItemBarcode: vi.fn(() => of({ barcode: 'IT-PO-000001' })),
    addItem: vi.fn(() => of({ id: 'item-2', name: 'New Item', barcode: 'IT-PO-000001' })),
  };

  let fixture: ComponentFixture<PurchaseOrderLineFormComponent>;
  let component: PurchaseOrderLineFormComponent;

  beforeEach(() => {
    catalog.filterByName.mockClear();
    catalog.findByName.mockClear();
    catalog.upsertEntry.mockClear();
    inventory.generateItemBarcode.mockReset();
    inventory.generateItemBarcode.mockReturnValue(of({ barcode: 'IT-PO-000001' }));
    inventory.addItem.mockClear();
    inventory.addItem.mockReturnValue(of({ id: 'item-2', name: 'New Item', barcode: 'IT-PO-000001' }));
    TestBed.configureTestingModule({
      imports: [PurchaseOrderLineFormComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: ProductCatalogSyncService, useValue: catalog },
        { provide: InventoryService, useValue: inventory },
      ],
    });
    fixture = TestBed.createComponent(PurchaseOrderLineFormComponent);
    component = fixture.componentInstance;
  });

  it('emits selected catalog item identity on submit', () => {
    const emitted: unknown[] = [];
    component.lineSubmitted.subscribe((line) => emitted.push(line));
    component.form.setValue({ description: 'Widget', expectedQuantity: 2, unitCost: 10 });

    component.submitLine();

    expect(emitted).toEqual([{ itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10 }]);
  });

  it('quick-creates product and emits the created item identity', async () => {
    const emitted: unknown[] = [];
    component.lineSubmitted.subscribe((line) => emitted.push(line));
    component.form.setValue({ description: 'New Item', expectedQuantity: 3, unitCost: 12 });

    await component.quickCreateProduct();

    expect(inventory.addItem).toHaveBeenCalledWith(expect.objectContaining({
      name: 'New Item',
      barcode: 'IT-PO-000001',
    }));
    expect(catalog.upsertEntry).toHaveBeenCalledWith({ itemId: 'item-2', name: 'New Item', barcode: 'IT-PO-000001' });
    expect(emitted).toEqual([{ itemId: 'item-2', description: 'New Item', expectedQuantity: 3, unitCost: 12 }]);
  });

  it('shows an error and does not emit when quick-create fails', async () => {
    const emitted: unknown[] = [];
    inventory.addItem.mockReturnValue(throwError(() => new Error('failed')));
    component.lineSubmitted.subscribe((line) => emitted.push(line));
    component.form.setValue({ description: 'New Item', expectedQuantity: 3, unitCost: 12 });

    await component.quickCreateProduct();

    expect(emitted).toEqual([]);
    expect(component.quickCreateError()).toBe('purchaseOrders.builder.quickCreateFailed');
  });
});
