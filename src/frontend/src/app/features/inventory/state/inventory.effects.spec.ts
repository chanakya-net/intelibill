import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { InventoryService } from '../services/inventory.service';
import { InventoryActions } from './inventory.actions';
import { InventoryEffects } from './inventory.effects';

describe('InventoryEffects', () => {
  let actions$: Subject<Action>;
  let effects: InventoryEffects;

  const inventoryService = {
    getItems: vi.fn<InventoryService['getItems']>(),
    addItem: vi.fn<InventoryService['addItem']>(),
  };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    inventoryService.getItems.mockReset();
    inventoryService.addItem.mockReset();

    TestBed.configureTestingModule({
      providers: [
        InventoryEffects,
        { provide: InventoryService, useValue: inventoryService },
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

  it('dispatches loadItemsSucceeded on load success', async () => {
    inventoryService.getItems.mockReturnValue(
      of([
        {
          id: 'item-1',
          name: 'Milk',
          barcode: 'B001',
          description: null,
          uom: 'ltr',
          isActive: true,
          currentStock: 10,
          hsnCode: null,
          defaultTaxRatePercent: 0,
          defaultTaxIncluded: false,
        },
      ])
    );

    const output = firstValueFrom(effects.loadItems$.pipe(take(1)));

    actions$.next(InventoryActions.loadItemsRequested());

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
            hsnCode: null,
            defaultTaxRatePercent: 0,
            defaultTaxIncluded: false,
          },
        ],
      })
    );
  });
});
