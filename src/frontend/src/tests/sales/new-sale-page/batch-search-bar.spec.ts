import { TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: batch search bar', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('updates search term via input event', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    const vm = component.vm;

    component.onBatchSearchTermChanged('oreo');
    expect(vm.searchInput()).toBe('oreo');
  });

  it('shows batch picker when multiple batches returned', () => {
    deps.saleService.getSellables.mockReturnValueOnce({
      subscribe: ({ next }: any) =>
        next([
          {
            kind: 'Goods',
            inventoryBatchId: 'batch-1',
            barcode: 'A',
            itemName: 'Oreo',
            batchNumber: 'B-01',
            quantity: 10,
            salesPrice: 50,
            mrp: 60,
            taxRatePercent: 18,
            taxIncluded: true,
            expiryDate: null,
            hsnCode: null,
          },
          {
            kind: 'Goods',
            inventoryBatchId: 'batch-2',
            barcode: 'B',
            itemName: 'Biscuit',
            batchNumber: 'B-02',
            quantity: 10,
            salesPrice: 50,
            mrp: 60,
            taxRatePercent: 18,
            taxIncluded: true,
            expiryDate: null,
            hsnCode: null,
          },
        ]),
    } as any);

    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    const vm = component.vm;

    vm.searchInput.set('b');
    void vm.onBarcodeSearch();

    expect(vm.showBatchPicker()).toBe(true);
    expect(vm.availableBatches().length).toBe(2);
  });

  it('builds suggestions from unified sellables, including services', () => {
    deps.saleService.getSellables.mockReturnValueOnce(
      of([
        {
          kind: 'Goods',
          inventoryBatchId: 'batch-1',
          barcode: 'A',
          itemName: 'Oreo',
          batchNumber: 'B-01',
          quantity: 10,
          salesPrice: 50,
          mrp: 60,
          taxRatePercent: 18,
          taxIncluded: true,
          expiryDate: null,
          hsnCode: null,
        },
        {
          kind: 'Service',
          serviceId: 'svc-1',
          code: 'S-001',
          name: 'Bike wash',
          description: null,
          price: 100,
          hsnCode: null,
          taxRatePercent: 0,
          taxIncluded: false,
        },
      ])
    );

    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    const vm = component.vm;

    component.onBatchSearchSuggestionFilter('wa');

    expect(deps.saleService.getSellables).toHaveBeenCalledWith('wa');
    expect(vm.searchSuggestions()).toEqual(['Oreo', 'A', 'Bike wash', 'S-001']);
  });

  it('ignores stale sellables responses when a newer query is in flight', () => {
    let firstNext: ((value: unknown) => void) | undefined;
    let secondNext: ((value: unknown) => void) | undefined;

    deps.saleService.getSellables
      .mockReturnValueOnce(
        new Observable((subscriber) => {
          firstNext = subscriber.next.bind(subscriber);
          return () => undefined;
        })
      )
      .mockReturnValueOnce(
        new Observable((subscriber) => {
          secondNext = subscriber.next.bind(subscriber);
          return () => undefined;
        })
      );

    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    const vm = component.vm;

    component.onBatchSearchSuggestionFilter('o');
    component.onBatchSearchSuggestionFilter('or');

    if (secondNext) {
      secondNext([
        {
          kind: 'Goods',
          inventoryBatchId: 'batch-2',
          barcode: 'OR2',
          itemName: 'Oreo 2',
          batchNumber: 'B-02',
          quantity: 10,
          salesPrice: 50,
          mrp: 60,
          taxRatePercent: 18,
          taxIncluded: true,
          expiryDate: null,
          hsnCode: null,
        },
      ]);
    }

    if (firstNext) {
      firstNext([
        {
          kind: 'Goods',
          inventoryBatchId: 'batch-1',
          barcode: 'OLD',
          itemName: 'Old Oreo',
          batchNumber: 'B-01',
          quantity: 10,
          salesPrice: 50,
          mrp: 60,
          taxRatePercent: 18,
          taxIncluded: true,
          expiryDate: null,
          hsnCode: null,
        },
      ]);
    }

    expect(vm.searchSuggestions()).toEqual(['Oreo 2', 'OR2']);
  });
});
