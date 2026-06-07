import { Injectable, inject, signal } from '@angular/core';

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
  private readonly storage = inject(PurchaseOrderDraftIndexedDbService);

  readonly header = signal<PurchaseOrderDraftHeader>(emptyHeader());
  readonly lines = signal<readonly CreatePurchaseOrderLineRequest[]>([]);
  readonly loadingDraft = signal(false);
  readonly duplicateBlocked = signal(false);

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
    if (shopId) {
      await this.storage.clearDraft(shopId);
    }
  }

  async replaceFromServer(shopId: string, order: PurchaseOrderDetail): Promise<void> {
    this.header.set({
      ...emptyHeader(),
      purchaseOrderId: order.purchaseOrderId,
      notes: order.notes,
    });
    this.lines.set(order.lines.map((line) => ({
      description: line.description,
      expectedQuantity: line.expectedQuantity,
      unitCost: line.unitCost,
    })));
    await this.saveDraft(shopId);
  }

  async updateHeader(shopId: string, update: Partial<PurchaseOrderDraftHeader>): Promise<void> {
    this.header.update((current) => ({ ...current, ...update }));
    await this.saveDraft(shopId);
  }

  async addOrMergeLine(shopId: string, line: CreatePurchaseOrderLineRequest): Promise<'added' | 'merged'> {
    const description = line.description.trim();
    const existing = this.lines().find((candidate) => sameDescription(candidate.description, description));
    if (existing) {
      this.lines.update((lines) => lines.map((candidate) => sameDescription(candidate.description, description)
        ? { ...candidate, expectedQuantity: candidate.expectedQuantity + line.expectedQuantity, unitCost: line.unitCost }
        : candidate));
      await this.saveDraft(shopId);
      return 'merged';
    }

    this.lines.update((lines) => [...lines, { ...line, description }]);
    await this.saveDraft(shopId);
    return 'added';
  }

  async removeLine(shopId: string, description: string): Promise<void> {
    this.lines.update((lines) => lines.filter((line) => !sameDescription(line.description, description)));
    await this.saveDraft(shopId);
  }

  toPayload(): CreatePurchaseOrderDraftRequest {
    return {
      notes: this.header().notes,
      lines: this.lines(),
    };
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

function sameDescription(left: string, right: string): boolean {
  return left.trim().toLocaleLowerCase() === right.trim().toLocaleLowerCase();
}
