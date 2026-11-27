import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { BarcodeLabelPrintCandidate, BARCODE_LABEL_MAX_TOTAL_QUANTITY, BarcodeLabelPrintDialogComponent } from './barcode-label-print-dialog.component';

function createItems(): readonly BarcodeLabelPrintCandidate[] {
  return [
    {
      itemId: 'item-1',
      itemName: 'Rice',
      barcode: 'RC-100',
      inventoryBatchId: null,
      quantity: 12,
    },
    {
      itemId: 'item-2',
      itemName: 'Milk',
      barcode: 'ML-200',
      inventoryBatchId: 'batch-1',
      quantity: 7,
    },
  ];
}

describe('BarcodeLabelPrintDialogComponent', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [BarcodeLabelPrintDialogComponent, TranslocoTestingModule.forRoot({ langs: { 'en-IN': {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BarcodeLabelPrintDialogComponent);
    const component = fixture.componentInstance;
    component.visible = true;
    component.items = createItems();
    fixture.detectChanges();

    return { fixture, component };
  }

  it('initializes every row with its provided quantity', () => {
    const { component } = setup();

    expect(component.rows()).toHaveLength(2);
    expect(component.rows()[0].quantity).toBe(12);
    expect(component.rows()[1].quantity).toBe(7);
  });

  it('emits a valid print request when all quantities are valid and within limit', () => {
    const { component } = setup();
    const printSpy = vi.fn();
    component.printRequested.subscribe(printSpy);
    component.onQuantityChange(0, 5);
    component.onQuantityChange(1, 3);

    component.onPrint();

    expect(printSpy).toHaveBeenCalledTimes(1);
    const request = printSpy.mock.calls[0]?.[0];
    expect(request).toEqual({
      items: [
        { itemId: 'item-1', quantity: 5, inventoryBatchId: null },
        { itemId: 'item-2', quantity: 3, inventoryBatchId: 'batch-1' },
      ],
    });
    expect(component.canPrint).toBe(true);
    expect(component.validationMessage()).toBe('');
  });

  it('blocks emitting request when any row quantity is not a positive integer', () => {
    const { component } = setup();
    const printSpy = vi.fn();
    component.printRequested.subscribe(printSpy);

    component.onQuantityChange(0, 0);
    component.onPrint();

    expect(printSpy).toHaveBeenCalledTimes(0);
    expect(component.validationMessage()).toBe('inventory.barcodeLabels.validation.invalidQuantity');
    expect(component.canPrint).toBe(false);
  });

  it('blocks emitting request when total quantity exceeds limit', () => {
    const { component } = setup();
    const printSpy = vi.fn();
    component.printRequested.subscribe(printSpy);

    component.onQuantityChange(0, BARCODE_LABEL_MAX_TOTAL_QUANTITY);
    component.onQuantityChange(1, 1);
    component.onPrint();

    expect(printSpy).toHaveBeenCalledTimes(0);
    expect(component.validationMessage()).toBe('inventory.barcodeLabels.validation.totalLimitExceeded');
    expect(component.canPrint).toBe(false);
  });
});
