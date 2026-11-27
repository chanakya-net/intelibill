import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: quick product tiles', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders up to four quick tiles with name, stock, and sales price', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.availableBatches.set([
      {
        barcode: 'A',
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
      },
      {
        barcode: 'B',
        itemName: 'Biscuit',
        batchNumber: 'B-02',
        inventoryBatchId: 'batch-2',
        quantity: 8,
        salesPrice: 25,
        mrp: 35,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
      },
      {
        barcode: 'C',
        itemName: 'Chocolate',
        batchNumber: 'B-03',
        inventoryBatchId: 'batch-3',
        quantity: 12,
        salesPrice: 90,
        mrp: 100,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
      },
      {
        barcode: 'D',
        itemName: 'Milk',
        batchNumber: 'B-04',
        inventoryBatchId: 'batch-4',
        quantity: 4,
        salesPrice: 35,
        mrp: 40,
        taxRatePercent: 0,
        taxIncluded: false,
        expiryDate: null,
      },
      {
        barcode: 'E',
        itemName: 'Sugar',
        batchNumber: 'B-05',
        inventoryBatchId: 'batch-5',
        quantity: 15,
        salesPrice: 10,
        mrp: 15,
        taxRatePercent: 0,
        taxIncluded: false,
        expiryDate: null,
      },
    ]);

    fixture.detectChanges();
    const tiles = fixture.nativeElement.querySelectorAll('.quick-product-tile');

    expect(tiles).toHaveLength(4);
    expect(tiles[0].textContent).toContain('Oreo');
    expect(tiles[0].textContent).toContain('10');
    expect(tiles[0].textContent).toContain('₹50.00');
  });

  it('adds batch to cart directly when tile maps to a single available batch', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.availableBatches.set([
      {
        barcode: 'X',
        itemName: 'Milkshake',
        batchNumber: 'MS-1',
        inventoryBatchId: 'batch-100',
        quantity: 6,
        salesPrice: 75,
        mrp: 80,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
      },
    ]);

    fixture.detectChanges();

    const tile = fixture.nativeElement.querySelector('.quick-product-tile') as HTMLButtonElement;
    tile.click();

    expect(vm.cart()).toHaveLength(1);
    expect(vm.cart()[0].inventoryBatchId).toBe('batch-100');
    expect(vm.showBatchPicker()).toBe(false);
    expect(vm.availableBatches()).toHaveLength(0);
  });

  it('opens batch picker when the product has multiple matching batches', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.availableBatches.set([
      {
        barcode: 'P',
        itemName: 'Tea',
        batchNumber: 'T-1',
        inventoryBatchId: 'batch-200',
        quantity: 3,
        salesPrice: 20,
        mrp: 30,
        taxRatePercent: 5,
        taxIncluded: true,
        expiryDate: null,
      },
      {
        barcode: 'P',
        itemName: 'Tea',
        batchNumber: 'T-2',
        inventoryBatchId: 'batch-201',
        quantity: 5,
        salesPrice: 22,
        mrp: 30,
        taxRatePercent: 5,
        taxIncluded: true,
        expiryDate: null,
      },
      {
        barcode: 'Q',
        itemName: 'Juice',
        batchNumber: 'J-1',
        inventoryBatchId: 'batch-202',
        quantity: 7,
        salesPrice: 40,
        mrp: 45,
        taxRatePercent: 5,
        taxIncluded: true,
        expiryDate: null,
      },
    ]);

    fixture.detectChanges();

    const tile = fixture.nativeElement.querySelector('.quick-product-tile') as HTMLButtonElement;
    tile.click();

    expect(vm.showBatchPicker()).toBe(true);
    expect(vm.cart()).toHaveLength(0);
    expect(vm.availableBatches()).toHaveLength(2);
    expect(vm.availableBatches()[0].batchNumber).toBe('T-1');
    expect(vm.availableBatches()[1].batchNumber).toBe('T-2');
  });
});
