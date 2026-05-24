import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import type { AuthSession } from '../../../core/auth/auth.models';
import { SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import type { AvailableBatchDto } from '../../inventory/services/inventory.service';
import { CartItem, SaleCartStateService } from './sale-cart-state.service';

describe('SaleCartStateService', () => {
  const session = signal<AuthSession | null>(makeSession('shop-1'));
  const cartStorage = {
    loadCart: vi.fn<SalesCartIndexedDbService['loadCart']>(),
    saveCart: vi.fn<SalesCartIndexedDbService['saveCart']>(),
    clearCart: vi.fn<SalesCartIndexedDbService['clearCart']>(),
  };

  function setup(): SaleCartStateService {
    TestBed.configureTestingModule({
      providers: [
        SaleCartStateService,
        { provide: AuthService, useValue: { session } },
        { provide: SalesCartIndexedDbService, useValue: cartStorage },
      ],
    });

    return TestBed.inject(SaleCartStateService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    session.set(makeSession('shop-1'));
    cartStorage.loadCart.mockResolvedValue([]);
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

    await service.loadPersistedCart(300_000, (row) => appliedDefaults.push(row));

    expect(cartStorage.loadCart).toHaveBeenCalledWith('shop-1', 300_000);
    expect(service.cartBootstrapped()).toBe(true);
    expect(service.cart()).toEqual(stored);
    expect(appliedDefaults).toHaveLength(1);

    await service.persistCart();
    expect(cartStorage.saveCart).toHaveBeenCalledWith('shop-1', stored);

    service.onClearCart();
    await service.persistCart();
    expect(cartStorage.clearCart).toHaveBeenCalledWith('shop-1');
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

  function makeSession(shopId: string | null): AuthSession {
    return {
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: {
        id: 'user-1',
        email: 'test@example.com',
        phoneNumber: null,
        firstName: 'Test',
        lastName: 'User',
      },
      activeShopId: shopId,
      shops: [],
    };
  }

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
