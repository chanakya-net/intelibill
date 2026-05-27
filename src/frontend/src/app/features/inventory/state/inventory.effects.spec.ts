import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, Subject, defer, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import type { InventoryCatalogQuery } from '../services/inventory.models';
import { InventoryService } from '../services/inventory.service';
import { InventoryActions } from './inventory.actions';
import { InventoryEffects } from './inventory.effects';

describe('InventoryEffects', () => {
  let actions$: Subject<Action>;
  let effects: InventoryEffects;
  let latestQuery: InventoryCatalogQuery = {
    search: '',
    status: 'all',
    pageNumber: 1,
    pageSize: 20,
  };

  const inventoryService = {
    getItems: vi.fn<InventoryService['getItems']>(),
    addItem: vi.fn<InventoryService['addItem']>(),
    updateItem: vi.fn<InventoryService['updateItem']>(),
  };

  const store = {
    select: vi.fn(() => defer(() => of(latestQuery))),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    latestQuery = {
      search: '',
      status: 'all',
      pageNumber: 1,
      pageSize: 20,
    };
    inventoryService.getItems.mockReset();
    inventoryService.addItem.mockReset();
    inventoryService.updateItem.mockReset();
    store.select.mockImplementation(() => defer(() => of(latestQuery)));

    TestBed.configureTestingModule({
      providers: [
        InventoryEffects,
        { provide: InventoryService, useValue: inventoryService },
        { provide: Store, useValue: store },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(InventoryEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  it('dispatches addItemSucceeded on add success', async () => {
    inventoryService.addItem.mockReturnValue(
      of({
        id: 'item-1',
        name: 'Milk',
        barcode: 'B001',
        description: null,
        uom: 'ltr',
        isActive: true,
        currentStock: 0,
        unitPrice: 0,
        currentStockValue: 0,
        reorderLevel: 0,
        stockStatus: 'critical',
        hsnCode: '0401',
        defaultTaxRatePercent: 5,
        defaultTaxIncluded: false,
      })
    );

    const output = firstValueFrom(effects.addItem$.pipe(take(1)));

    actions$.next(
      InventoryActions.addItemRequested({
        payload: {
          name: 'Milk',
          barcode: 'B001',
          description: null,
          uom: 'ltr',
          isActive: true,
          hsnCode: '0401',
          defaultTaxRatePercent: 5,
        },
      })
    );

    await expect(output).resolves.toEqual(
      InventoryActions.addItemSucceeded({
        item: {
          id: 'item-1',
          name: 'Milk',
          barcode: 'B001',
          description: null,
          uom: 'ltr',
          isActive: true,
          currentStock: 0,
          unitPrice: 0,
          currentStockValue: 0,
          reorderLevel: 0,
          stockStatus: 'critical',
          hsnCode: '0401',
          defaultTaxRatePercent: 5,
          defaultTaxIncluded: false,
        },
      })
    );
  });

  it('maps active-shop error on add failure', async () => {
    inventoryService.addItem.mockReturnValue(
      throwError(() => ({ error: { title: 'Shop.ActiveShopNotSelected' } }))
    );

    const output = firstValueFrom(effects.addItem$.pipe(take(1)));

    actions$.next(
      InventoryActions.addItemRequested({
        payload: {
          name: 'Milk',
          barcode: 'B001',
          description: null,
          uom: 'ltr',
          isActive: true,
          hsnCode: null,
          defaultTaxRatePercent: 0,
        },
      })
    );

    await expect(output).resolves.toEqual(
      InventoryActions.addItemFailed({
        errorMessage: 'errors.items.activeShopNotSelected',
      })
    );
  });

  it('loads items with query params and paginated response', async () => {
    inventoryService.getItems.mockReturnValue(
      of({
        items: [
          {
            id: 'item-1',
            name: 'Milk',
            barcode: 'B001',
            description: null,
            uom: 'ltr',
            isActive: true,
            currentStock: 10,
            unitPrice: 45,
            currentStockValue: 450,
            reorderLevel: 5,
            stockStatus: 'inStock',
            hsnCode: null,
            defaultTaxRatePercent: 0,
            defaultTaxIncluded: false,
          },
        ],
        totalCount: 12,
        pageNumber: 2,
        pageSize: 10,
        summary: {
          totalItems: 12,
          activeItems: 11,
          inactiveItems: 1,
          runningLowStockCount: 3,
          criticalStockCount: 1,
          totalStockValue: 4500,
        },
      })
    );

    const output = firstValueFrom(effects.loadItems$.pipe(take(1)));

    actions$.next(
      InventoryActions.loadItemsRequested({
        query: {
          search: 'milk',
          status: 'active',
          pageNumber: 2,
          pageSize: 10,
        },
      })
    );

    await expect(output).resolves.toEqual(
      InventoryActions.loadItemsSucceeded({
        items: [
          {
            id: 'item-1',
            name: 'Milk',
            barcode: 'B001',
            description: null,
            uom: 'ltr',
            isActive: true,
            currentStock: 10,
            unitPrice: 45,
            currentStockValue: 450,
            reorderLevel: 5,
            stockStatus: 'inStock',
            hsnCode: null,
            defaultTaxRatePercent: 0,
            defaultTaxIncluded: false,
          },
        ],
        totalCount: 12,
        pageNumber: 2,
        pageSize: 10,
        summary: {
          totalItems: 12,
          activeItems: 11,
          inactiveItems: 1,
          runningLowStockCount: 3,
          criticalStockCount: 1,
          totalStockValue: 4500,
        },
      })
    );
    expect(inventoryService.getItems).toHaveBeenCalledWith({
      search: 'milk',
      status: 'active',
      pageNumber: 2,
      pageSize: 10,
    });
  });

  it('reloads the latest catalog query after add success', async () => {
    latestQuery = {
      search: 'bread',
      status: 'inactive',
      pageNumber: 4,
      pageSize: 15,
    };
    inventoryService.addItem.mockReturnValue(
      of({
        id: 'item-1',
        name: 'Bread',
        barcode: 'B001',
        description: null,
        uom: 'pcs',
        isActive: false,
        currentStock: 0,
        unitPrice: 0,
        currentStockValue: 0,
        reorderLevel: 0,
        stockStatus: 'critical',
        hsnCode: null,
        defaultTaxRatePercent: 0,
        defaultTaxIncluded: false,
      })
    );

    const output = firstValueFrom(effects.reloadItemsAfterAdd$.pipe(take(1)));

    actions$.next(
      InventoryActions.addItemSucceeded({
        item: {
          id: 'item-1',
          name: 'Bread',
          barcode: 'B001',
          description: null,
          uom: 'pcs',
          isActive: false,
          currentStock: 0,
          unitPrice: 0,
          currentStockValue: 0,
          reorderLevel: 0,
          stockStatus: 'critical',
          hsnCode: null,
          defaultTaxRatePercent: 0,
          defaultTaxIncluded: false,
        },
      })
    );

    await expect(output).resolves.toEqual(
      InventoryActions.loadItemsRequested({
        query: {
          search: 'bread',
          status: 'inactive',
          pageNumber: 4,
          pageSize: 15,
        },
      })
    );
  });

  it('reloads the latest catalog query after update success', async () => {
    latestQuery = {
      search: 'milk',
      status: 'active',
      pageNumber: 2,
      pageSize: 10,
    };

    const output = firstValueFrom(effects.reloadItemsAfterUpdate$.pipe(take(1)));

    actions$.next(InventoryActions.updateItemSucceeded());

    await expect(output).resolves.toEqual(
      InventoryActions.loadItemsRequested({
        query: {
          search: 'milk',
          status: 'active',
          pageNumber: 2,
          pageSize: 10,
        },
      })
    );
  });
});
