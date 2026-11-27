import { effect } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ScopeOwner } from '../../../core/context/shop-scope';
import { AppReloadService } from '../../../core/services/app-reload.service';
import { SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import type { AvailableBatchDto } from '../../inventory/services/inventory.models';
import { CartItem, SaleCartStateService } from './sale-cart-state.service';

describe('SaleCartStateService', () => {
  const ownerA: ScopeOwner = { userId: 'user-1', shopId: 'shop-1' };
  const ownerB: ScopeOwner = { userId: 'user-1', shopId: 'shop-2' };
  const cartStorage = {
    loadCart: vi.fn<SalesCartIndexedDbService['loadCart']>(),
    loadServiceCart: vi.fn<SalesCartIndexedDbService['loadServiceCart']>(),
    saveCart: vi.fn<SalesCartIndexedDbService['saveCart']>(),
    clearCart: vi.fn<SalesCartIndexedDbService['clearCart']>(),
  };

  function setup(): SaleCartStateService {
    TestBed.configureTestingModule({
      providers: [
        SaleCartStateService,
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
        { provide: SalesCartIndexedDbService, useValue: cartStorage },
      ],
    });

    return TestBed.inject(SaleCartStateService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    cartStorage.loadCart.mockResolvedValue([]);
    cartStorage.loadServiceCart.mockResolvedValue([]);
    cartStorage.saveCart.mockResolvedValue(undefined);
    cartStorage.clearCart.mockResolvedValue(undefined);
    vi.clearAllMocks();
  });

  it('adds batches and enforces quantity limits', () => {
    const service = setup();
    const batch = makeBatch({ quantity: 5 });

    const firstAdd = service.addBatchToCart(batch, 2);
    expect(firstAdd.added).toBe(true);
    expect(firstAdd.addedLineKey).toBeTruthy();
    expect(service.cart()).toHaveLength(1);
    expect(service.cart()[0].quantity).toBe(2);

    const secondAdd = service.addBatchToCart(batch, 3);
    expect(secondAdd.added).toBe(true);
    expect(service.cart()[0].quantity).toBe(5);

    const blockedAdd = service.addBatchToCart(batch, 1);
    expect(blockedAdd.added).toBe(false);
    expect(service.cart()[0].quantity).toBe(5);
  });

  it('mutates cart items via increase, decrease, remove, and clear', () => {
    const service = setup();
    const batch = makeBatch({ quantity: 10 });
    service.addBatchToCart(batch, 1);

    service.onIncreaseCartItem(0);
    expect(service.cart()[0].quantity).toBe(2);

    service.onDecreaseCartItem(0);
    service.onDecreaseCartItem(0);
    expect(service.cart()[0].quantity).toBe(1);

    service.onRemoveCartItem(0);
    expect(service.cart()).toHaveLength(0);

    service.addBatchToCart(batch, 1);
    service.onClearCart();
    expect(service.cart()).toHaveLength(0);
  });

  it('updates cart item discount, hsn, and tax overrides', () => {
    const service = setup();
    service.addBatchToCart(makeBatch(), 1);
    const [item] = service.cart();

    service.setItemDiscountType(item.clientLineKey, 1);
    service.setItemDiscountValue(item.clientLineKey, 12.5);
    service.setItemHsnCode(item.clientLineKey, '1234');
    service.setItemTaxRatePercent(item.clientLineKey, 5);

    expect(service.cart()[0]).toMatchObject({
      itemDiscountType: 1,
      itemDiscountValue: 12.5,
      hsnCode: '1234',
      taxRatePercent: 5,
    });

    service.setItemDiscountType(item.clientLineKey, 0);
    expect(service.cart()[0]).toMatchObject({
      itemDiscountType: 0,
      itemDiscountValue: 0,
    });
  });

  it('applies preview revalidation adjustments and only patches missing hsn codes', () => {
    const service = setup();
    service.addBatchToCart(makeBatch({ inventoryBatchId: 'batch-1' }), 1);
    service.addBatchToCart(makeBatch({ inventoryBatchId: 'batch-2', batchNumber: 'B-2' }), 1);
    const [firstItem, secondItem] = service.cart();

    service.setItemHsnCode(firstItem.clientLineKey, '5678');
    service.applyDiscountAdjustments([
      {
        clientLineKey: firstItem.clientLineKey,
        nextType: 2,
        nextValue: 15,
      },
      {
        clientLineKey: secondItem.clientLineKey,
        nextType: 1,
        nextValue: 5,
      },
    ]);
    service.applyResolvedHsnCodeIfMissing(firstItem.clientLineKey, '9999');
    service.applyResolvedHsnCodeIfMissing(secondItem.clientLineKey, '1234');

    expect(service.cart()[0]).toMatchObject({
      itemDiscountType: 2,
      itemDiscountValue: 15,
      hsnCode: '5678',
    });
    expect(service.cart()[1]).toMatchObject({
      itemDiscountType: 1,
      itemDiscountValue: 5,
      hsnCode: '1234',
    });
  });

  it('loads persisted cart and persists changes', async () => {
    const service = setup();
    const stored: CartItem[] = [{
      clientLineKey: 'line-1',
      barcode: 'BC-1',
      itemName: 'Item',
      batchNumber: 'B-1',
      inventoryBatchId: 'batch-1',
      quantity: 1,
      availableQuantity: 5,
      salesPrice: 100,
      mrp: 100,
      taxRatePercent: 18,
      taxIncluded: true,
      costPrice: 50,
      itemDiscountType: 0,
      itemDiscountValue: 0,
      hsnCode: null,
      uom: 'PCS',
      quantityScale: 0,
      documentUnit: 'NOS',
    }];

    cartStorage.loadCart.mockResolvedValue(stored);
    const appliedDefaults: CartItem[] = [];

    await service.loadPersistedCart(ownerA, 300_000, (row) => appliedDefaults.push(row));

    expect(cartStorage.loadCart).toHaveBeenCalledWith(ownerA, 300_000);
    expect(service.cartBootstrapped()).toBe(true);
    expect(service.cart()).toEqual(stored);
    expect(appliedDefaults).toHaveLength(1);

    await service.persistCart();
    expect(cartStorage.saveCart).toHaveBeenCalledWith(ownerA, stored, []);

    service.onClearCart();
    await service.persistCart();
    expect(cartStorage.clearCart).toHaveBeenCalledWith(ownerA);
  });

  describe('ownership', () => {
    function enter(owner: ScopeOwner): ShopContextService {
      const context = TestBed.inject(ShopContextService);
      context.initialize(owner);
      return context;
    }

    it('saves the cart under its owner and clears memory the moment the scope moves', async () => {
      const service = setup();
      const context = enter(ownerA);
      const rowsA = [makeCartItem({ clientLineKey: 'a-1', inventoryBatchId: 'batch-a' })];
      cartStorage.loadCart.mockResolvedValue(rowsA);
      await service.loadPersistedCart(ownerA, 300_000);

      context.transition(ownerB, 'cross-tab', () => undefined);

      expect(service.cart()).toEqual([]);
      expect(service.owner()).toBeNull();
      expect(service.cartBootstrapped()).toBe(false);
      expect(cartStorage.saveCart).toHaveBeenCalledWith(ownerA, rowsA, []);
    });

    it('a_b_nonempty_drafts_restore_independently: a delayed load for the shop left never lands in the next one', async () => {
      const service = setup();
      const context = enter(ownerA);
      const rowsA = [makeCartItem({ clientLineKey: 'a-1', inventoryBatchId: 'batch-a' })];
      const rowsB = [makeCartItem({ clientLineKey: 'b-1', inventoryBatchId: 'batch-b' })];
      let releaseA!: (rows: CartItem[]) => void;
      cartStorage.loadCart.mockImplementation((owner) =>
        owner.shopId === ownerA.shopId
          ? new Promise((resolve) => (releaseA = resolve))
          : Promise.resolve(rowsB),
      );

      const loadingA = service.loadPersistedCart(ownerA, 300_000);
      context.transition(ownerB, 'cross-tab', () => undefined);
      await service.loadPersistedCart(ownerB, 300_000);
      releaseA(rowsA);
      await loadingA;

      expect(service.cart()).toEqual(rowsB);
      expect(service.owner()).toEqual(ownerB);
      await service.persistCart();
      expect(cartStorage.saveCart).toHaveBeenCalledWith(ownerB, rowsB, []);
      expect(cartStorage.saveCart).not.toHaveBeenCalledWith(ownerA, expect.anything(), expect.anything());
      expect(cartStorage.saveCart).not.toHaveBeenCalledWith(ownerB, rowsA, expect.anything());
    });

    it('logout_clears_memory_not_ownership: the next user starts empty and the first keeps their draft', async () => {
      const service = setup();
      const context = enter(ownerA);
      const rowsA = [makeCartItem({ clientLineKey: 'a-1' })];
      cartStorage.loadCart.mockImplementation((owner) =>
        Promise.resolve(owner.userId === ownerA.userId ? rowsA : []),
      );
      await service.loadPersistedCart(ownerA, 300_000);

      context.transition(null, 'sign-out', () => undefined);
      expect(service.cart()).toEqual([]);
      expect(cartStorage.saveCart).toHaveBeenCalledWith(ownerA, rowsA, []);

      const nextUser = { userId: 'user-2', shopId: ownerA.shopId };
      context.transition(nextUser, 'sign-in', () => undefined);
      await service.loadPersistedCart(nextUser, 300_000);

      expect(cartStorage.loadCart).toHaveBeenLastCalledWith(nextUser, 300_000);
      expect(service.cart()).toEqual([]);
      expect(cartStorage.clearCart).not.toHaveBeenCalledWith(ownerA);
    });

    it('failed_save_is_visible: a cart that cannot be saved stays in memory and says so', async () => {
      const service = setup();
      enter(ownerA);
      await service.loadPersistedCart(ownerA, 300_000);
      service.addBatchToCart(makeBatch(), 1);
      cartStorage.saveCart.mockRejectedValue(new DOMException('quota', 'QuotaExceededError'));

      await expect(service.persistCart()).resolves.toBe(false);

      expect(service.draftSaveFailed()).toBe(true);
      expect(service.cart()).toHaveLength(1);

      cartStorage.saveCart.mockResolvedValue(undefined);
      await expect(service.persistCart()).resolves.toBe(true);
      expect(service.draftSaveFailed()).toBe(false);
    });

    it('never writes over a stored draft it could not read', async () => {
      const service = setup();
      enter(ownerA);
      cartStorage.loadCart.mockRejectedValue(new Error('The cart is open in an older tab; reload it to continue.'));

      await service.loadPersistedCart(ownerA, 300_000);
      service.addBatchToCart(makeBatch(), 1);

      await expect(service.persistCart()).resolves.toBe(false);
      expect(service.draftSaveFailed()).toBe(true);
      expect(service.cartBootstrapped()).toBe(true);
      expect(cartStorage.saveCart).not.toHaveBeenCalled();
      expect(cartStorage.clearCart).not.toHaveBeenCalled();
    });

    it('lets an empty cart switch even when storage is failing', async () => {
      const service = setup();
      const context = enter(ownerA);
      await service.loadPersistedCart(ownerA, 300_000);
      cartStorage.clearCart.mockRejectedValue(new Error('storage unavailable'));

      await expect(context.prepareToLeave()).resolves.toEqual({ ok: true });
    });

    it('keeps the tab in its shop when the cart cannot be saved before a switch', async () => {
      const service = setup();
      const context = enter(ownerA);
      await service.loadPersistedCart(ownerA, 300_000);
      service.addBatchToCart(makeBatch(), 1);
      cartStorage.saveCart.mockRejectedValue(new Error('storage unavailable'));

      await expect(context.prepareToLeave()).resolves.toEqual({ ok: false, reason: 'draft-save-failed' });
      expect(service.cart()).toHaveLength(1);
    });
  });

  describe('quantities', () => {
    /** Saves the cart whenever it changes, the way the new-sale page does. */
    async function persistOnChange(service: SaleCartStateService): Promise<void> {
      TestBed.runInInjectionContext(() =>
        effect(() => {
          service.cart();
          service.serviceCart();
          if (service.cartBootstrapped()) {
            void service.persistCart();
          }
        }),
      );
      await service.loadPersistedCart(ownerA, 300_000);
      TestBed.tick();
      await Promise.resolve();
      cartStorage.saveCart.mockClear();
      cartStorage.clearCart.mockClear();
    }

    function countCartWrites(service: SaleCartStateService): () => number {
      const update = vi.spyOn(service.cart, 'update');
      const set = vi.spyOn(service.cart, 'set');
      return () => update.mock.calls.length + set.mock.calls.length;
    }

    it('edit_quantity_sets_exact_fraction: a kg line takes 0.25 exactly in one draft write', async () => {
      const service = setup();
      await persistOnChange(service);
      service.addBatchToCart(makeBatch({ uom: 'kg', quantityScale: 3, quantity: 5 }), 1);
      TestBed.tick();
      await Promise.resolve();
      cartStorage.saveCart.mockClear();
      const [line] = service.cart();

      expect(service.setQuantity(line.clientLineKey, 0.25)).toBe('ok');
      TestBed.tick();
      await Promise.resolve();

      expect(service.cart()[0].quantity).toBe(0.25);
      expect(cartStorage.saveCart).toHaveBeenCalledTimes(1);
      expect(cartStorage.saveCart.mock.calls[0][1][0]).toMatchObject({ quantity: 0.25, uom: 'kg', quantityScale: 3 });
    });

    it('large_change_is_one_operation: 1 to 40 is one state update', () => {
      const service = setup();
      service.addBatchToCart(makeBatch({ quantity: 50 }), 1);
      const writes = countCartWrites(service);

      expect(service.setQuantity(service.cart()[0].clientLineKey, 40)).toBe('ok');

      expect(writes()).toBe(1);
      expect(service.cart()[0].quantity).toBe(40);
    });

    it('invalid_numeric_input_is_rejected: half a piece leaves the line unchanged', () => {
      const service = setup();
      service.addBatchToCart(makeBatch({ uom: 'PCS', quantityScale: 0 }), 2);
      const before = service.cart();
      const writes = countCartWrites(service);

      expect(service.setQuantity(before[0].clientLineKey, 0.5)).toBe('tooPrecise');

      expect(service.cart()).toBe(before);
      expect(writes()).toBe(0);
    });

    it('refuses more than the stock, a non-positive value, an unknown line and a no-op', () => {
      const service = setup();
      service.addBatchToCart(makeBatch({ quantity: 5, uom: 'kg', quantityScale: 3 }), 1);
      const before = service.cart();
      const key = before[0].clientLineKey;
      const writes = countCartWrites(service);

      expect(service.setQuantity(key, 5.001)).toBe('exceedsStock');
      expect(service.setQuantity(key, 0)).toBe('notPositive');
      expect(service.setQuantity(key, -1)).toBe('notPositive');
      expect(service.setQuantity(key, Number.NaN)).toBe('notPositive');
      expect(service.setQuantity(key, 0.0001)).toBe('tooPrecise');
      expect(service.setQuantity(key, 1)).toBe('unchanged');
      expect(service.setQuantity('missing', 1)).toBe('notFound');

      expect(service.cart()).toBe(before);
      expect(writes()).toBe(0);
      expect(service.setQuantity(key, 5)).toBe('ok');
    });

    it('piece_increment_is_one: plus and minus move one unit for every unit', () => {
      const service = setup();
      service.addBatchToCart(makeBatch({ inventoryBatchId: 'pcs', uom: 'PCS', quantityScale: 0, quantity: 10 }), 1);
      service.addBatchToCart(makeBatch({ inventoryBatchId: 'kg', uom: 'kg', quantityScale: 3, quantity: 10 }), 0.25);

      service.onIncreaseCartItem(0);
      service.onIncreaseCartItem(1);
      expect(service.cart().map((line) => line.quantity)).toEqual([2, 1.25]);

      service.onIncreaseCartItem(1);
      service.onDecreaseCartItem(1);
      service.onDecreaseCartItem(1);
      expect(service.cart()[1].quantity).toBe(0.25);
      service.onDecreaseCartItem(1);
      expect(service.cart()[1].quantity).toBe(0.25);
    });

    it('minus stays available while one unit less still leaves at least one step', () => {
      const service = setup();
      service.cart.set([
        makeCartItem({ clientLineKey: 'a', quantity: 1, quantityScale: 0 }),
        makeCartItem({ clientLineKey: 'b', quantity: 2, quantityScale: 0 }),
        makeCartItem({ clientLineKey: 'c', quantity: 1.001, quantityScale: 3 }),
        makeCartItem({ clientLineKey: 'd', quantity: 1, quantityScale: 3 }),
        makeCartItem({ clientLineKey: 'e', quantity: 1.1, quantityScale: 1 }),
        makeCartItem({ clientLineKey: 'f', quantity: 0.5, quantityScale: 3 }),
      ]);

      expect([0, 1, 2, 3, 4, 5].map((index) => service.canDecreaseCartItem(index))).toEqual([
        false, true, true, false, true, false,
      ]);
      expect(service.canDecreaseCartItem(9)).toBe(false);

      service.onDecreaseCartItem(4);
      expect(service.cart()[4].quantity).toBe(0.1);
    });

    it('plus is offered only when one more unit is in stock', () => {
      const service = setup();

      expect(service.canIncreaseCartItem(makeCartItem({ quantity: 0.5, availableQuantity: 1.2, quantityScale: 3 }))).toBe(false);
      expect(service.canIncreaseCartItem(makeCartItem({ quantity: 0.5, availableQuantity: 1.5, quantityScale: 3 }))).toBe(true);
      expect(service.canIncreaseCartItem(makeCartItem({ quantity: 4, availableQuantity: 5 }))).toBe(true);
      expect(service.canIncreaseCartItem(makeCartItem({ quantity: 5, availableQuantity: 5 }))).toBe(false);
    });

    it('adds 0.1 then 0.2 of one batch as exactly 0.3', () => {
      const service = setup();
      const batch = makeBatch({ uom: 'kg', quantityScale: 1, quantity: 0.3 });

      service.addBatchToCart(batch, 0.1);
      const second = service.addBatchToCart(batch, 0.2);

      expect(second.added).toBe(true);
      expect(service.cart()).toHaveLength(1);
      expect(service.cart()[0].quantity).toBe(0.3);
    });

    it('carries the unit, precision and document unit of the batch onto the line', () => {
      const service = setup();

      service.addBatchToCart(makeBatch({ inventoryBatchId: 'kg', uom: 'kg', quantityScale: 3, documentUnit: 'KGS' }), 1);
      service.addBatchToCart(makeBatch({ inventoryBatchId: 'old' }), 1);

      expect(service.cart()[0]).toMatchObject({ uom: 'kg', quantityScale: 3, documentUnit: 'KGS' });
      expect(service.cart()[1]).toMatchObject({ uom: '', quantityScale: 0, documentUnit: 'NOS' });
    });

    it('refuses to add a quantity the unit cannot hold, without truncating it', () => {
      const service = setup();

      expect(service.addBatchToCart(makeBatch({ uom: 'PCS', quantityScale: 0 }), 0.5)).toEqual({
        added: false,
        addedLineKey: null,
        refusal: 'tooPrecise',
      });
      expect(service.addBatchToCart(makeBatch(), 0)).toMatchObject({ added: false, refusal: 'notPositive' });
      expect(service.addBatchToCart(makeBatch({ quantity: 5 }), 6)).toMatchObject({ added: false, refusal: 'exceedsStock' });
      expect(service.cart()).toEqual([]);
    });

    it('keeps service quantities whole, refusing a fraction instead of truncating it', () => {
      const service = setup();
      const key = service.addServiceToCart({
        kind: 'Service',
        serviceId: 'svc-1',
        code: 'S1',
        name: 'Repair',
        description: null,
        price: 100,
        hsnCode: null,
        taxRatePercent: 18,
        taxIncluded: false,
      });

      expect(service.setServiceQuantity(key, 2.5)).toBe('tooPrecise');
      expect(service.setServiceQuantity(key, 0)).toBe('notPositive');
      expect(service.serviceCart()[0].quantity).toBe(1);
      expect(service.setServiceQuantity(key, 3)).toBe('ok');
      expect(service.serviceCart()[0].quantity).toBe(3);
      expect(service.setServiceQuantity(key, 3)).toBe('unchanged');
      expect(service.setServiceQuantity('missing', 3)).toBe('notFound');
    });
  });

  it('calculates line totals for included and excluded tax prices', () => {
    const service = setup();
    const included = makeCartItem({
      salesPrice: 118,
      taxRatePercent: 18,
      taxIncluded: true,
      quantity: 2,
    });
    const excluded = makeCartItem({
      salesPrice: 100,
      taxRatePercent: 18,
      taxIncluded: false,
      quantity: 1,
    });

    expect(service.getLineSubtotal(included)).toBeCloseTo(200, 6);
    expect(service.getLineTaxAmount(included)).toBeCloseTo(36, 6);
    expect(service.getLineTotal(included)).toBeCloseTo(236, 6);

    expect(service.getLineSubtotal(excluded)).toBeCloseTo(100, 6);
    expect(service.getLineTaxAmount(excluded)).toBeCloseTo(18, 6);
    expect(service.getLineTotal(excluded)).toBeCloseTo(118, 6);
  });

  function makeBatch(overrides: Partial<AvailableBatchDto> = {}): AvailableBatchDto {
    return {
      barcode: 'BC-1',
      itemName: 'Item',
      batchNumber: 'B-1',
      inventoryBatchId: 'batch-1',
      quantity: 5,
      salesPrice: 100,
      mrp: 100,
      taxRatePercent: 18,
      taxIncluded: true,
      expiryDate: null,
      ...overrides,
    };
  }

  function makeCartItem(overrides: Partial<CartItem> = {}): CartItem {
    return {
      clientLineKey: 'line-1',
      barcode: 'BC-1',
      itemName: 'Item',
      batchNumber: 'B-1',
      inventoryBatchId: 'batch-1',
      quantity: 1,
      availableQuantity: 5,
      salesPrice: 100,
      mrp: 100,
      taxRatePercent: 18,
      taxIncluded: true,
      costPrice: 50,
      itemDiscountType: 0,
      itemDiscountValue: 0,
      hsnCode: null,
      uom: 'PCS',
      quantityScale: 0,
      documentUnit: 'NOS',
      ...overrides,
    };
  }
});
