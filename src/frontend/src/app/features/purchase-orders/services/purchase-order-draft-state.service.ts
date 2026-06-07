import { Injectable, effect, inject, signal } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import {
  PurchaseOrderDraftIndexedDbService,
  type PurchaseOrderDraftRecord,
} from '../../../core/storage/purchase-order-draft-indexeddb.service';
import type {
  CreatePurchaseOrderDraftRequest,
  CreatePurchaseOrderLineRequest,
  PurchaseOrderDetail,
} from './purchase-order.service';

export interface PurchaseOrderDraftHeader {
  readonly purchaseOrderId: string | null;
  readonly supplier: { readonly id: string; readonly name: string } | null;
  readonly orderDate: string | null;
  readonly expectedDeliveryDate: string | null;
  readonly supplierReferenceNumber: string | null;
  readonly notes: string | null;
}

@Injectable({ providedIn: 'root' })
export class PurchaseOrderDraftStateService {
  private readonly authService = inject(AuthService);
  private readonly storage = inject(PurchaseOrderDraftIndexedDbService);
  private activeShopId: string | null = null;

  readonly header = signal<PurchaseOrderDraftHeader>(emptyHeader());
  readonly lines = signal<readonly CreatePurchaseOrderLineRequest[]>([]);
  readonly loadingDraft = signal(false);
  readonly duplicateBlocked = signal(false);
  readonly restoredPurchaseOrderId = signal<string | null>(null);
  readonly hasRestoredLocalDraft = signal(false);

  constructor() {
    effect(() => {
      const shopId = this.authService.session()?.activeShopId ?? null;
      if (shopId === this.activeShopId) return;
      this.resetInMemory();
      this.activeShopId = shopId;
      if (shopId) void this.loadDraft(shopId);
    });
  }

  async loadDraft(shopId: string): Promise<PurchaseOrderDraftRecord | null> {
    this.loadingDraft.set(true);
    try {
      const record = await this.storage.loadDraft(shopId);
      if (record) {
        this.header.set({
          purchaseOrderId: record.purchaseOrderId,
          supplier: record.supplier,
          orderDate: record.orderDate,
          expectedDeliveryDate: record.expectedDeliveryDate,
          supplierReferenceNumber: record.supplierReferenceNumber,
          notes: record.notes,
        });
        this.lines.set(record.lines);
        this.restoredPurchaseOrderId.set(record.purchaseOrderId);
        this.hasRestoredLocalDraft.set(true);
      } else {
        this.resetInMemory();
      }
      return record;
    } finally {
      this.loadingDraft.set(false);
    }
  }

  async saveDraft(shopId: string): Promise<void> {
    if (!shopId) return;

    await this.storage.saveDraft({
      shopId,
      ...this.header(),
      lines: this.lines(),
      updatedAt: new Date().toISOString(),
    });
  }

  async clearDraft(shopId: string): Promise<void> {
    this.header.set(emptyHeader());
    this.lines.set([]);
    this.duplicateBlocked.set(false);
    this.restoredPurchaseOrderId.set(null);
    this.hasRestoredLocalDraft.set(false);
    if (shopId) {
      await this.storage.clearDraft(shopId);
    }
  }

  replaceFromServer(order: PurchaseOrderDetail): void {
    this.header.set({
      ...emptyHeader(),
      purchaseOrderId: order.purchaseOrderId,
      supplier: order.supplierId ? { id: order.supplierId, name: '' } : null,
      orderDate: order.orderDate,
      expectedDeliveryDate: order.expectedDeliveryDate,
      supplierReferenceNumber: order.supplierReferenceNumber,
      notes: order.notes,
    });
    this.lines.set(order.lines.map((line) => ({
      itemId: line.itemId,
      description: line.description,
      expectedQuantity: line.expectedQuantity,
      unitCost: line.unitCost,
    })));
    this.restoredPurchaseOrderId.set(order.purchaseOrderId);
    this.hasRestoredLocalDraft.set(false);
  }

  async updateHeader(shopId: string, update: Partial<PurchaseOrderDraftHeader>): Promise<void> {
    this.header.update((current) => ({ ...current, ...update }));
    this.restoredPurchaseOrderId.set(this.header().purchaseOrderId);
    this.hasRestoredLocalDraft.set(true);
    await this.saveDraft(shopId);
  }

  resolveSupplierName(supplierId: string, name: string): void {
    this.header.update((current) => {
      if (current.supplier?.id !== supplierId || current.supplier.name) {
        return current;
      }

      return { ...current, supplier: { id: supplierId, name } };
    });
  }

  async addOrMergeLine(shopId: string, line: CreatePurchaseOrderLineRequest): Promise<'added' | 'merged'> {
    const description = line.description.trim();
    const existing = this.lines().find((candidate) => candidate.itemId === line.itemId);
    if (existing) {
      this.lines.update((lines) => lines.map((candidate) => candidate.itemId === line.itemId
        ? { ...candidate, expectedQuantity: candidate.expectedQuantity + line.expectedQuantity, unitCost: line.unitCost }
        : candidate));
      this.hasRestoredLocalDraft.set(true);
      await this.saveDraft(shopId);
      return 'merged';
    }

    this.lines.update((lines) => [...lines, { ...line, description }]);
    this.hasRestoredLocalDraft.set(true);
    await this.saveDraft(shopId);
    return 'added';
  }

  async removeLine(shopId: string, itemId: string): Promise<void> {
    this.lines.update((lines) => lines.filter((line) => line.itemId !== itemId));
    this.hasRestoredLocalDraft.set(true);
    await this.saveDraft(shopId);
  }

  toPayload(): CreatePurchaseOrderDraftRequest {
    const header = this.header();
    return {
      supplierId: header.supplier?.id ?? null,
      orderDate: header.orderDate,
      expectedDeliveryDate: header.expectedDeliveryDate,
      supplierReferenceNumber: header.supplierReferenceNumber,
      notes: header.notes,
      lines: this.lines(),
    };
  }

  private resetInMemory(): void {
    this.header.set(emptyHeader());
    this.lines.set([]);
    this.duplicateBlocked.set(false);
    this.restoredPurchaseOrderId.set(null);
    this.hasRestoredLocalDraft.set(false);
  }
}

function emptyHeader(): PurchaseOrderDraftHeader {
  return {
    purchaseOrderId: null,
    supplier: null,
    orderDate: null,
    expectedDeliveryDate: null,
    supplierReferenceNumber: null,
    notes: null,
  };
}
