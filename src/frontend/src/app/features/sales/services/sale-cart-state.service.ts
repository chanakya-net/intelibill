import { Injectable, inject, signal } from '@angular/core';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { ScopeOwner, sameOwner } from '../../../core/context/shop-scope';
import { SalesCartDraftItem, SalesCartDraftServiceItem, SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import type { AvailableBatchDto } from '../../inventory/services/inventory.models';
import type { SellableServiceDto, ServiceCartLine } from './sale.models';

export interface CartItem extends SalesCartDraftItem {}

export type { ServiceCartLine } from './sale.models';

export interface AddBatchResult {
  readonly added: boolean;
  readonly addedLineKey: string | null;
}

export interface CartDiscountAdjustment {
  readonly clientLineKey: string;
  readonly nextType: 0 | 1 | 2;
  readonly nextValue: number;
}

/**
 * The cart being built on this device, and the user and shop it belongs to.
 *
 * The rows in memory always belong to `owner`, captured when they were loaded; they are saved under
 * that owner whatever the session says by then. On any change of user or shop the cart detaches:
 * a load still running for the old owner is dropped, the rows are saved under their owner and
 * cleared from memory before the next scope can read or overwrite them.
 */
@Injectable({ providedIn: 'root' })
export class SaleCartStateService {
  private readonly cartStorage = inject(SalesCartIndexedDbService);
  private readonly shopContext = inject(ShopContextService);
  private cartLoadToken = 0;
  private readonly ownerSignal = signal<ScopeOwner | null>(null);

  readonly cart = signal<CartItem[]>([]);
  readonly serviceCart = signal<ServiceCartLine[]>([]);
  readonly cartBootstrapped = signal(false);
  /** Who the rows in memory belong to; null until a cart is loaded. */
  readonly owner = this.ownerSignal.asReadonly();
  /** The last save of this owner's cart failed; the rows are only in memory. */
  readonly draftSaveFailed = signal(false);

  constructor() {
    this.shopContext.onDetach(() => void this.detach());
    this.shopContext.onLeave(() => this.persistCart());
  }

  async loadPersistedCart(
    owner: ScopeOwner,
    cartRetentionMs: number,
    onApplyDefaults?: (item: CartItem) => void,
  ): Promise<void> {
    if (!sameOwner(this.ownerSignal(), owner)) {
      // Rows of another owner never stay in memory beside this one's.
      void this.detach();
    }

    const token = ++this.cartLoadToken;
    this.cartBootstrapped.set(false);

    if (!owner.userId || !owner.shopId) {
      this.ownerSignal.set(null);
      this.cartBootstrapped.set(true);
      return;
    }

    this.ownerSignal.set(owner);
    try {
      const [rows, serviceRows] = await Promise.all([
        this.cartStorage.loadCart(owner, cartRetentionMs),
        this.cartStorage.loadServiceCart(owner, cartRetentionMs),
      ]);
      if (token !== this.cartLoadToken) {
        return;
      }

      if (this.cart().length === 0) {
        this.cart.set([...rows]);
        if (onApplyDefaults) {
          rows
            .filter((row) => !this.normalizeHsnCode(row.hsnCode))
            .forEach((row) => onApplyDefaults(row));
        }
      }

      if (this.serviceCart().length === 0) {
        this.serviceCart.set([...serviceRows]);
      }
    } catch {
      // Nothing could be read: the cart starts from what is in memory.
    } finally {
      if (token === this.cartLoadToken) {
        this.cartBootstrapped.set(true);
      }
    }
  }

  /** Saves the rows in memory under their owner. Resolves false when the save failed. */
  async persistCart(): Promise<boolean> {
    const owner = this.ownerSignal();
    if (!owner || !this.cartBootstrapped()) {
      return true;
    }

    return this.writeDraft(owner, this.cart(), this.serviceCart());
  }

  /**
   * Leaves the current owner: drops any load still running for it, clears the rows from memory and
   * saves them under that owner. Synchronous up to the save, so nothing of the old owner remains in
   * memory once it returns; resolves false when the save failed.
   */
  detach(): Promise<boolean> {
    const owner = this.ownerSignal();
    const bootstrapped = this.cartBootstrapped();
    const cart = this.cart();
    const serviceCart = this.serviceCart();

    this.cartLoadToken++;
    this.ownerSignal.set(null);
    this.cart.set([]);
    this.serviceCart.set([]);
    this.cartBootstrapped.set(false);
    this.draftSaveFailed.set(false);

    if (!owner || !bootstrapped) {
      return Promise.resolve(true);
    }

    return this.writeDraft(owner, cart, serviceCart);
  }

  private async writeDraft(
    owner: ScopeOwner,
    cart: readonly CartItem[],
    serviceCart: readonly ServiceCartLine[],
  ): Promise<boolean> {
    let saved = true;
    try {
      if (cart.length === 0 && serviceCart.length === 0) {
        await this.cartStorage.clearCart(owner);
      } else {
        await this.cartStorage.saveCart(
          owner,
          cart,
          serviceCart.map((svc): SalesCartDraftServiceItem => ({
            kind: 'service',
            clientLineKey: svc.clientLineKey,
            serviceId: svc.serviceId,
            serviceName: svc.serviceName,
            serviceCode: svc.serviceCode,
            quantity: svc.quantity,
            unitPrice: svc.unitPrice,
            taxRatePercent: svc.taxRatePercent,
            taxIncluded: svc.taxIncluded,
            hsnCode: svc.hsnCode,
          })),
        );
      }
    } catch {
      saved = false;
    }

    // Reported only to the owner that is still in memory; a detached owner's page is gone.
    if (sameOwner(this.ownerSignal(), owner)) {
      this.draftSaveFailed.set(!saved);
    }
    return saved;
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

  addServiceToCart(service: SellableServiceDto): string {
    const clientLineKey = crypto.randomUUID();
    this.serviceCart.update((items) => [
      ...items,
      {
        kind: 'service',
        clientLineKey,
        serviceId: service.serviceId,
        serviceName: service.name,
        serviceCode: service.code,
        quantity: 1,
        unitPrice: service.price,
        taxRatePercent: service.taxRatePercent,
        taxIncluded: service.taxIncluded,
        hsnCode: service.hsnCode,
      },
    ]);
    return clientLineKey;
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

  onIncreaseServiceItem(clientLineKey: string): void {
    this.serviceCart.update((items) =>
      items.map((item) =>
        item.clientLineKey === clientLineKey
          ? { ...item, quantity: item.quantity + 1 }
          : item
      )
    );
  }

  onDecreaseServiceItem(clientLineKey: string): void {
    this.serviceCart.update((items) =>
      items.map((item) =>
        item.clientLineKey === clientLineKey && item.quantity > 1
          ? { ...item, quantity: item.quantity - 1 }
          : item
      )
    );
  }

  onRemoveServiceItem(clientLineKey: string): void {
    this.serviceCart.update((items) => items.filter((item) => item.clientLineKey !== clientLineKey));
  }

  setServiceUnitPrice(clientLineKey: string, value: number): void {
    const price = Number.isFinite(value) && value >= 0 ? Number(value.toFixed(2)) : 0;
    this.serviceCart.update((items) =>
      items.map((item) =>
        item.clientLineKey === clientLineKey ? { ...item, unitPrice: price } : item
      )
    );
  }

  setServiceQuantity(clientLineKey: string, value: number): void {
    const qty = Math.max(1, Math.trunc(Number(value)));
    if (!Number.isFinite(qty)) return;
    this.serviceCart.update((items) =>
      items.map((item) =>
        item.clientLineKey === clientLineKey ? { ...item, quantity: qty } : item
      )
    );
  }

  onClearCart(): void {
    this.cart.set([]);
    this.serviceCart.set([]);
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

  getServiceLineSubtotal(item: ServiceCartLine): number {
    return this.getServiceUnitSubtotal(item) * item.quantity;
  }

  getServiceLineTaxAmount(item: ServiceCartLine): number {
    return this.getServiceUnitTaxAmount(item) * item.quantity;
  }

  getServiceLineTotal(item: ServiceCartLine): number {
    return this.getServiceUnitFinalPrice(item) * item.quantity;
  }

  getUnitSubtotal(item: CartItem): number {
    return this.getUnitSubtotalFrom(item.salesPrice, item.taxRatePercent, item.taxIncluded);
  }

  getUnitTaxAmount(item: CartItem): number {
    return this.getUnitTaxAmountFrom(item.salesPrice, item.taxRatePercent, item.taxIncluded);
  }

  getUnitFinalPrice(item: CartItem): number {
    return this.getUnitFinalPriceFrom(item.salesPrice, item.taxRatePercent, item.taxIncluded);
  }

  getServiceUnitSubtotal(item: ServiceCartLine): number {
    return this.getUnitSubtotalFrom(item.unitPrice, item.taxRatePercent, item.taxIncluded);
  }

  getServiceUnitTaxAmount(item: ServiceCartLine): number {
    return this.getUnitTaxAmountFrom(item.unitPrice, item.taxRatePercent, item.taxIncluded);
  }

  getServiceUnitFinalPrice(item: ServiceCartLine): number {
    return this.getUnitFinalPriceFrom(item.unitPrice, item.taxRatePercent, item.taxIncluded);
  }

  private getUnitSubtotalFrom(price: number, taxRatePercent: number, taxIncluded: boolean): number {
    if (taxRatePercent <= 0) {
      return price;
    }

    if (!taxIncluded) {
      return price;
    }

    return price / (1 + taxRatePercent / 100);
  }

  private getUnitTaxAmountFrom(price: number, taxRatePercent: number, taxIncluded: boolean): number {
    if (taxRatePercent <= 0) {
      return 0;
    }

    const basePrice = this.getUnitSubtotalFrom(price, taxRatePercent, taxIncluded);
    return (basePrice * taxRatePercent) / 100;
  }

  private getUnitFinalPriceFrom(price: number, taxRatePercent: number, taxIncluded: boolean): number {
    if (taxRatePercent <= 0) {
      return price;
    }

    if (taxIncluded) {
      return price;
    }

    return price + this.getUnitTaxAmountFrom(price, taxRatePercent, taxIncluded);
  }

  normalizeHsnCode(value: string | null | undefined): string | null {
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
