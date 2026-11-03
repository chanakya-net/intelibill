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
      ...overrides,
    };
  }
});
