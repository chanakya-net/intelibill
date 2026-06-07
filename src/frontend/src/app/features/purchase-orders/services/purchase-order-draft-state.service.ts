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
    if (shopId) {
      await this.storage.clearDraft(shopId);
    }
  }

  async replaceFromServer(shopId: string, order: PurchaseOrderDetail): Promise<void> {
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
    await this.saveDraft(shopId);
    this.restoredPurchaseOrderId.set(order.purchaseOrderId);
  }

  async updateHeader(shopId: string, update: Partial<PurchaseOrderDraftHeader>): Promise<void> {
    this.header.update((current) => ({ ...current, ...update }));
    await this.saveDraft(shopId);
  }

  async addOrMergeLine(shopId: string, line: CreatePurchaseOrderLineRequest): Promise<'added' | 'merged'> {
    const description = line.description.trim();
    const existing = this.lines().find((candidate) => candidate.itemId === line.itemId);
    if (existing) {
      this.lines.update((lines) => lines.map((candidate) => candidate.itemId === line.itemId
        ? { ...candidate, expectedQuantity: candidate.expectedQuantity + line.expectedQuantity, unitCost: line.unitCost }
        : candidate));
      await this.saveDraft(shopId);
      return 'merged';
    }

    this.lines.update((lines) => [...lines, { ...line, description }]);
    await this.saveDraft(shopId);
    return 'added';
  }

  async removeLine(shopId: string, itemId: string): Promise<void> {
    this.lines.update((lines) => lines.filter((line) => line.itemId !== itemId));
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
