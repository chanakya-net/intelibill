import { Injectable, inject, signal } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import { SalesCartDraftItem, SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import type { AvailableBatchDto } from '../../inventory/services/inventory.models';

export interface CartItem extends SalesCartDraftItem {}

export interface AddBatchResult {
  readonly added: boolean;
  readonly addedLineKey: string | null;
}

export interface CartDiscountAdjustment {
  readonly clientLineKey: string;
  readonly nextType: 0 | 1 | 2;
  readonly nextValue: number;
}

@Injectable({ providedIn: 'root' })
export class SaleCartStateService {
  private readonly cartStorage = inject(SalesCartIndexedDbService);
  private readonly authService = inject(AuthService);
  private cartLoadToken = 0;

  readonly cart = signal<CartItem[]>([]);
  readonly cartBootstrapped = signal(false);

  async loadPersistedCart(
    cartRetentionMs: number,
    onApplyDefaults?: (item: CartItem) => void,
  ): Promise<void> {
    const shopId = this.authService.session()?.activeShopId ?? '';
    const token = ++this.cartLoadToken;
    this.cartBootstrapped.set(false);

    if (!shopId) {
      this.cart.set([]);
      this.cartBootstrapped.set(true);
      return;
    }

    try {
      const rows = await this.cartStorage.loadCart(shopId, cartRetentionMs);
      if (token !== this.cartLoadToken) {
        return;
      }

      // Preserve in-memory edits made before the async cart bootstrap completes.
      if (this.cart().length === 0) {
        this.cart.set([...rows]);
        if (onApplyDefaults) {
          rows
            .filter((row) => !this.normalizeHsnCode(row.hsnCode))
            .forEach((row) => onApplyDefaults(row));
        }
      }
    } catch {
      if (token === this.cartLoadToken) {
        this.cart.set([]);
      }
    } finally {
      if (token === this.cartLoadToken) {
        this.cartBootstrapped.set(true);
      }
    }
  }

  async persistCart(): Promise<void> {
    const shopId = this.authService.session()?.activeShopId ?? '';
    if (!shopId || !this.cartBootstrapped()) {
      return;
    }

    const cart = this.cart();
    try {
      if (cart.length === 0) {
        await this.cartStorage.clearCart(shopId);
        return;
      }

      await this.cartStorage.saveCart(shopId, cart);
    } catch {
      // Ignore persistence errors; cart remains in memory.
    }
  }

  addBatchToCart(batch: AvailableBatchDto, quantityToAdd: number): AddBatchResult {
    const snapshotCostPrice = Number(batch.costPrice ?? 0);
    let added = false;
    let addedLineKey: string | null = null;

    this.cart.update((items) => {
      const existingIndex = items.findIndex(
        (item) => item.inventoryBatchId === batch.inventoryBatchId,
      );

      if (existingIndex >= 0) {
        const existing = items[existingIndex];
        const maxAvailable = Math.max(existing.availableQuantity, batch.quantity);
        const nextQuantity = existing.quantity + quantityToAdd;
        if (nextQuantity > maxAvailable) {
          return items;
        }

        added = true;
        return items.map((item, index) =>
          index === existingIndex
            ? {
                ...item,
                quantity: nextQuantity,
                availableQuantity: maxAvailable,
              }
            : item,
        );
      }

      if (quantityToAdd > batch.quantity) {
        return items;
      }

      added = true;
      const clientLineKey = crypto.randomUUID();
      addedLineKey = clientLineKey;
      return [
        ...items,
        {
          clientLineKey,
          barcode: batch.barcode,
          itemName: batch.itemName,
          batchNumber: batch.batchNumber,
          inventoryBatchId: batch.inventoryBatchId,
          quantity: quantityToAdd,
          availableQuantity: batch.quantity,
          salesPrice: batch.salesPrice,
          mrp: batch.mrp,
          taxRatePercent: batch.taxRatePercent,
          taxIncluded: batch.taxIncluded,
          costPrice: Number.isFinite(snapshotCostPrice) ? this.roundAmount(snapshotCostPrice) : 0,
          itemDiscountType: 0,
          itemDiscountValue: 0,
          hsnCode: this.normalizeHsnCode(batch.hsnCode),
        },
      ];
    });

    return { added, addedLineKey };
  }

  onIncreaseCartItem(index: number): void {
    this.cart.update((items) =>
      items.map((item, i) =>
        i === index && item.quantity < item.availableQuantity
          ? { ...item, quantity: item.quantity + 1 }
          : item
      )
    );
  }

  onDecreaseCartItem(index: number): void {
    this.cart.update((items) =>
      items.map((item, i) =>
        i === index && item.quantity > 1
          ? { ...item, quantity: item.quantity - 1 }
          : item
      )
    );
  }

  onRemoveCartItem(index: number): void {
    this.cart.update((items) => items.filter((_, i) => i !== index));
  }

  onClearCart(): void {
    this.cart.set([]);
  }

  setItemDiscountType(clientLineKey: string, type: 0 | 1 | 2): void {
    this.updateItem(clientLineKey, (item) => ({
      ...item,
      itemDiscountType: type,
      itemDiscountValue: type === 0 ? 0 : item.itemDiscountValue,
    }));
  }

  setItemDiscountValue(clientLineKey: string, value: number): void {
    this.updateItem(clientLineKey, (item) => ({ ...item, itemDiscountValue: value }));
  }

  setItemHsnCode(clientLineKey: string, value: string | null | undefined): void {
    const normalized = this.normalizeHsnCode(value);
    this.updateItem(clientLineKey, (item) => ({ ...item, hsnCode: normalized }));
  }

  setItemTaxRatePercent(clientLineKey: string, value: number): void {
    this.updateItem(clientLineKey, (item) => ({ ...item, taxRatePercent: value }));
  }

  applyDiscountAdjustments(adjustments: readonly CartDiscountAdjustment[]): void {
    if (adjustments.length === 0) {
      return;
    }

    const adjustmentsByKey = new Map(
      adjustments.map((adjustment) => [adjustment.clientLineKey, adjustment] as const),
    );

    this.cart.update((items) => {
      let changed = false;
      const nextItems = items.map((item) => {
        const adjustment = adjustmentsByKey.get(item.clientLineKey);
        if (!adjustment) {
          return item;
        }
        if (
          item.itemDiscountType === adjustment.nextType &&
          item.itemDiscountValue === adjustment.nextValue
        ) {
          return item;
        }

        changed = true;
        return {
          ...item,
          itemDiscountType: adjustment.nextType,
          itemDiscountValue: adjustment.nextValue,
        };
      });

      return changed ? nextItems : items;
    });
  }

  applyResolvedHsnCodeIfMissing(clientLineKey: string, value: string | null | undefined): void {
    const normalized = this.normalizeHsnCode(value);
    if (!normalized) {
      return;
    }

    this.cart.update((items) => {
      let changed = false;
      const nextItems = items.map((item) => {
        if (item.clientLineKey !== clientLineKey || this.normalizeHsnCode(item.hsnCode)) {
          return item;
        }

        changed = true;
        return { ...item, hsnCode: normalized };
      });

      return changed ? nextItems : items;
    });
  }

  hasTax(item: CartItem): boolean {
    return item.taxRatePercent > 0;
  }

  canIncreaseCartItem(item: CartItem): boolean {
    return item.quantity < item.availableQuantity;
  }

  getLineSubtotal(item: CartItem): number {
    return this.getUnitSubtotal(item) * item.quantity;
  }

  getLineTaxAmount(item: CartItem): number {
    return this.getUnitTaxAmount(item) * item.quantity;
  }

  getLineTotal(item: CartItem): number {
    return this.getUnitFinalPrice(item) * item.quantity;
  }

  getUnitSubtotal(item: CartItem): number {
    if (!this.hasTax(item)) {
      return item.salesPrice;
    }

    if (!item.taxIncluded) {
      return item.salesPrice;
    }

    return item.salesPrice / (1 + item.taxRatePercent / 100);
  }

  getUnitTaxAmount(item: CartItem): number {
    if (!this.hasTax(item)) {
      return 0;
    }

    const basePrice = this.getUnitSubtotal(item);
    return (basePrice * item.taxRatePercent) / 100;
  }

  getUnitFinalPrice(item: CartItem): number {
    if (!this.hasTax(item)) {
      return item.salesPrice;
    }

    if (item.taxIncluded) {
      return item.salesPrice;
    }

    return item.salesPrice + this.getUnitTaxAmount(item);
  }

  private normalizeHsnCode(value: string | null | undefined): string | null {
    const trimmed = (value ?? '').trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  private updateItem(clientLineKey: string, mutate: (item: CartItem) => CartItem): void {
    if (!clientLineKey) {
      return;
    }

    this.cart.update((items) => {
      let changed = false;
      const nextItems = items.map((item) => {
        if (item.clientLineKey !== clientLineKey) {
          return item;
        }

        changed = true;
        return mutate(item);
      });

      return changed ? nextItems : items;
    });
  }

  private roundAmount(value: number): number {
    return Number(value.toFixed(2));
  }
}
