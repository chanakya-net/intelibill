import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { InventoryService } from '../../../inventory/services/inventory.service';
import { DiscountTargetItemsComponent } from './discount-target-items.component';

describe('DiscountTargetItemsComponent', () => {
  const makeBatch = (
    overrides: Partial<{
      barcode: string;
      itemName: string;
      batchNumber: string;
      inventoryBatchId: string;
      quantity: number;
      salesPrice: number;
      mrp: number;
      taxRatePercent: number;
      taxIncluded: boolean;
      expiryDate: string | null;
    }> = {},
  ) => ({
    barcode: '111',
    itemName: 'Rice',
    batchNumber: 'BATCH-001',
    inventoryBatchId: 'batch-1',
    quantity: 5,
    salesPrice: 100,
    mrp: 120,
    taxRatePercent: 5,
    taxIncluded: false,
    expiryDate: null,
    ...overrides,
  });

  const inventoryService = {
    getAvailableBatchesBySearchTerm: vi.fn(),
  };

  beforeEach(() => {
    inventoryService.getAvailableBatchesBySearchTerm.mockReset();

    TestBed.configureTestingModule({
      imports: [
        DiscountTargetItemsComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [{ provide: InventoryService, useValue: inventoryService }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('requires at least three trimmed characters before searching', () => {
    const component = TestBed.createComponent(DiscountTargetItemsComponent).componentInstance;

    vi.useFakeTimers();
    try {
      component.onBatchSearchTermChange('ab');
      vi.advanceTimersByTime(350);
      expect(inventoryService.getAvailableBatchesBySearchTerm).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it('debounces batch search API calls by 300ms', () => {
    const component = TestBed.createComponent(DiscountTargetItemsComponent).componentInstance;
    inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(of([makeBatch()]));

    vi.useFakeTimers();
    try {
      component.onBatchSearchTermChange('ric');
      vi.advanceTimersByTime(299);
      expect(inventoryService.getAvailableBatchesBySearchTerm).not.toHaveBeenCalled();
      vi.advanceTimersByTime(1);

      expect(inventoryService.getAvailableBatchesBySearchTerm).toHaveBeenCalledWith('ric');
    } finally {
      vi.useRealTimers();
    }
  });

  it('keeps only latest in-flight batch search result', () => {
    const firstResults$ = new Subject<readonly ReturnType<typeof makeBatch>[]>();
    const secondResults$ = new Subject<readonly ReturnType<typeof makeBatch>[]>();
    const batchA = makeBatch({
      inventoryBatchId: 'batch-a',
      itemName: 'Alpha',
      batchNumber: 'A-1',
    });
    const batchB = makeBatch({ inventoryBatchId: 'batch-b', itemName: 'Beta', batchNumber: 'B-1' });
    const batchC = makeBatch({
      inventoryBatchId: 'batch-c',
      itemName: 'Gamma',
      batchNumber: 'C-1',
    });

    inventoryService.getAvailableBatchesBySearchTerm.mockImplementation((term: string) => {
      if (term === 'ric') return firstResults$;
      if (term === 'rice') return secondResults$;
      return of([]);
    });

    vi.useFakeTimers();
    try {
      const component = TestBed.createComponent(DiscountTargetItemsComponent).componentInstance;
      component.onBatchSearchTermChange('ric');
      vi.advanceTimersByTime(300);
      component.onBatchSearchTermChange('rice');
      vi.advanceTimersByTime(300);

      secondResults$.next([batchB, batchC]);
      secondResults$.complete();
      firstResults$.next([batchA]);
      firstResults$.complete();

      expect(inventoryService.getAvailableBatchesBySearchTerm).toHaveBeenCalledTimes(2);
      expect(component.batchSearchResults()).toEqual([batchB, batchC]);
      expect(component.batchSearchResults()).not.toContainEqual(batchA);
    } finally {
      vi.useRealTimers();
    }
  });

  it('auto-selects single search result', async () => {
    const fixture = TestBed.createComponent(DiscountTargetItemsComponent);
    const component = fixture.componentInstance;
    const selected: string[][] = [];
    component.selectionChange.subscribe((next) => selected.push([...next]));
    const batch = makeBatch();

    inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(of([batch]));
    vi.useFakeTimers();
    try {
      component.onBatchSearchTermChange('rice');
      vi.advanceTimersByTime(300);
      expect(inventoryService.getAvailableBatchesBySearchTerm).toHaveBeenCalledWith('rice');
      await Promise.resolve();
      expect(selected.at(-1)).toEqual(['batch-1']);
      expect(component.form.controls.inventoryBatchId.value).toBe('batch-1');
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows multiple results and keeps current selection blank', () => {
    const component = TestBed.createComponent(DiscountTargetItemsComponent).componentInstance;
    const batchA = makeBatch({ inventoryBatchId: 'batch-a', itemName: 'Rice', batchNumber: 'A-1' });
    const batchB = makeBatch({ inventoryBatchId: 'batch-b', itemName: 'Rice', batchNumber: 'B-1' });

    inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(of([batchA, batchB]));

    vi.useFakeTimers();
    try {
      component.form.controls.inventoryBatchId.setValue('batch-1');
      component.onBatchSearchTermChange('rice');
      vi.advanceTimersByTime(300);
      expect(component.batchSearchResults()).toEqual([batchA, batchB]);
      expect(component.form.controls.inventoryBatchId.value).toBe('');
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows no-result state and clears batch selection', () => {
    const component = TestBed.createComponent(DiscountTargetItemsComponent).componentInstance;
    component.form.controls.inventoryBatchId.setValue('batch-1');
    component.selectedBatchLabel.set('Rice · BATCH-001');

    inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(of([]));
    vi.useFakeTimers();
    try {
      component.onBatchSearchTermChange('none');
      vi.advanceTimersByTime(300);
      expect(inventoryService.getAvailableBatchesBySearchTerm).toHaveBeenCalledWith('none');
      expect(component.batchSearchResults()).toEqual([]);
      expect(component.batchSearchNoResults()).toBe(true);
      expect(component.form.controls.inventoryBatchId.value).toBe('');
      expect(component.selectedBatchLabel()).toBe('');
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows retryable feedback when batch search fails', () => {
    const fixture = TestBed.createComponent(DiscountTargetItemsComponent);
    const component = fixture.componentInstance;
    inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(
      throwError(() => new Error('search failed')),
    );

    vi.useFakeTimers();
    try {
      component.onBatchSearchTermChange('rice');
      vi.advanceTimersByTime(300);
      fixture.detectChanges();

      expect(component.batchSearchError()).toBe('discounts.errors.batchSearchFailed');
      expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('links touched target validation feedback to the batch search input', () => {
    const fixture = TestBed.createComponent(DiscountTargetItemsComponent);
    fixture.componentInstance.markAllAsTouched();
    fixture.detectChanges();

    const search = fixture.nativeElement.querySelector(
      '#discount-rule-batch-search',
    ) as HTMLInputElement;
    expect(search.getAttribute('aria-describedby')).toBe('discount-batch-selection-error');
    expect(fixture.nativeElement.querySelector('#discount-batch-selection-error')).not.toBeNull();
  });
});
