import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, Subject, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import { PurchaseOrderLineFormComponent } from './purchase-order-line-form.component';

describe('PurchaseOrderLineFormComponent', () => {
  const catalog = {
    filterByName: vi.fn(() => [{ itemId: 'item-1', name: 'Widget', barcode: 'W1' }]),
    findByName: vi.fn((name: string) =>
      name === 'Widget' ? { itemId: 'item-1', name: 'Widget', barcode: 'W1' } : undefined,
    ),
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
    inventory.addItem.mockReturnValue(
      of({ id: 'item-2', name: 'New Item', barcode: 'IT-PO-000001' }),
    );
    TestBed.configureTestingModule({
      imports: [
        PurchaseOrderLineFormComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
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

    expect(emitted).toEqual([
      { itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10 },
    ]);
  });

  it('rejects a whitespace-only line description', () => {
    component.form.patchValue({ description: '   ' });

    expect(component.form.controls.description.hasError('required')).toBe(true);
  });

  it('rejects a fractional expected quantity', () => {
    component.form.patchValue({ expectedQuantity: 1.5 });

    expect(component.form.controls.expectedQuantity.hasError('integer')).toBe(true);
  });

  it('renders quantity as a horizontal stepper distinct from unit cost', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const qty = host.querySelector('.po-line-form__qty-input');
    const inputs = host.querySelectorAll('p-inputnumber');

    expect(qty).toBeTruthy();
    expect(qty?.classList.contains('p-inputnumber-horizontal')).toBe(true);
    expect(inputs.length).toBe(2);
  });

  it('updates item autocomplete suggestions from the query', () => {
    catalog.filterByName.mockReturnValue([
      { itemId: 'item-1', name: 'Widget', barcode: 'W1' },
      { itemId: 'item-2', name: 'Wide Box', barcode: 'W2' },
    ]);

    component.onItemSearch({ query: 'wid' } as never);

    expect(catalog.filterByName).toHaveBeenCalledWith('wid');
    expect(component.itemSuggestions()).toEqual(['Widget', 'Wide Box']);
  });

  it('quick-creates product and emits the created item identity', async () => {
    const emitted: unknown[] = [];
    component.lineSubmitted.subscribe((line) => emitted.push(line));
    component.form.setValue({ description: 'New Item', expectedQuantity: 3, unitCost: 12 });

    await component.quickCreateProduct();

    expect(inventory.addItem).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'New Item',
        barcode: 'IT-PO-000001',
      }),
    );
    expect(catalog.upsertEntry).toHaveBeenCalledWith({
      itemId: 'item-2',
      name: 'New Item',
      barcode: 'IT-PO-000001',
    });
    expect(emitted).toEqual([
      { itemId: 'item-2', description: 'New Item', expectedQuantity: 3, unitCost: 12 },
    ]);
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

  it('shows the authoritative expiry without losing the pending line or upserting a catalog item', async () => {
    const draft = { description: 'New Item', expectedQuantity: 3, unitCost: 12 };
    const submitted = vi.fn();
    component.lineSubmitted.subscribe(submitted);
    TestBed.inject(ShopContextService).initialize({ userId: 'owner', shopId: 'shop' });
    component.form.setValue(draft);
    inventory.addItem.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 403, error: { code: 'Licence.Expired' },
    })));

    await component.quickCreateProduct();

    expect(component.definitionAccess.blockingMessage()).toBe('billing.status.tradingAuthority.expired');
    expect(component.form.getRawValue()).toEqual(draft);
    expect(catalog.upsertEntry).not.toHaveBeenCalled();
    expect(submitted).not.toHaveBeenCalled();
  });

  it('avoids barcode allocation for known restrictions and still emits a selected item', async () => {
    const submitted = vi.fn();
    component.lineSubmitted.subscribe(submitted);
    TestBed.inject(ShopContextService).initialize({ userId: 'owner', shopId: 'shop' });
    component.definitionAccess.reportRefusal(new HttpErrorResponse({
      status: 403, error: { code: 'Licence.PendingActivation' },
    }), component.definitionAccess.scope());
    component.form.setValue({ description: 'Widget', expectedQuantity: 2, unitCost: 10 });
    await component.quickCreateProduct();
    expect(inventory.generateItemBarcode).not.toHaveBeenCalled();
    expect(inventory.addItem).not.toHaveBeenCalled();

    component.submitLine();
    expect(submitted).toHaveBeenCalledWith({
      itemId: 'item-1', description: 'Widget', expectedQuantity: 2, unitCost: 10,
    });
  });

  it('checks the restriction again after asynchronous barcode allocation', async () => {
    const barcode = new Subject<{ barcode: string }>();
    inventory.generateItemBarcode.mockReturnValue(barcode.asObservable());
    TestBed.inject(ShopContextService).initialize({ userId: 'owner', shopId: 'shop' });
    component.form.setValue({ description: 'New Item', expectedQuantity: 3, unitCost: 12 });
    const quickCreate = component.quickCreateProduct();

    component.definitionAccess.reportRefusal(new HttpErrorResponse({
      status: 503, error: { code: 'Licence.VerificationUnavailable' },
    }), component.definitionAccess.scope());
    barcode.next({ barcode: 'IT-PO-000001' });
    await quickCreate;
    expect(inventory.addItem).not.toHaveBeenCalled();
    expect(catalog.upsertEntry).not.toHaveBeenCalled();
    expect(component.form.controls.description.value).toBe('New Item');
  });

});
