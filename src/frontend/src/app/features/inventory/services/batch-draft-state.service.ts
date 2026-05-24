import { Injectable, inject, signal } from '@angular/core';

import { AuthService } from '../../../core/auth/auth.service';
import {
  InventoryDraftIndexedDbService,
  InventoryInboundDraftRow,
} from '../../../core/storage/inventory-draft-indexeddb.service';

@Injectable({ providedIn: 'root' })
export class BatchDraftStateService {
  private readonly draftStorage = inject(InventoryDraftIndexedDbService);
  private readonly authService = inject(AuthService);

  readonly pendingRows = signal<readonly InventoryInboundDraftRow[]>([]);
  readonly loadingDraft = signal(false);

  private get shopId(): string {
    return this.authService.session()?.activeShopId ?? '';
  }

  async loadDraft(shopId: string): Promise<void> {
    this.loadingDraft.set(true);
    try {
      const rows = await this.draftStorage.loadRows(shopId);
      this.pendingRows.set(rows);
    } finally {
      this.loadingDraft.set(false);
    }
  }

  async saveDraftRow(row: InventoryInboundDraftRow): Promise<void> {
    const existing = this.pendingRows().find((r) => r.clientRowId === row.clientRowId);
    const updatedRows = existing
      ? this.pendingRows().map((r) => (r.clientRowId === row.clientRowId ? row : r))
      : [...this.pendingRows(), row];
    this.pendingRows.set(updatedRows);
    await this.persistRows(this.shopId, updatedRows);
  }

  async removeDraftRow(clientRowId: string): Promise<void> {
    const updatedRows = this.pendingRows().filter((r) => r.clientRowId !== clientRowId);
    this.pendingRows.set(updatedRows);
    await this.persistRows(this.shopId, updatedRows);
  }

  async clearDraft(shopId: string): Promise<void> {
    this.pendingRows.set([]);
    await this.draftStorage.clearRows(shopId);
  }

  async replaceRows(shopId: string, rows: readonly InventoryInboundDraftRow[]): Promise<void> {
    this.pendingRows.set(rows);
    await this.persistRows(shopId, rows);
  }

  async incrementRowQuantity(barcode: string): Promise<InventoryInboundDraftRow | null> {
    const matching = this.pendingRows().find((r) => r.barcode === barcode);
    if (!matching) {
      return null;
    }
    const updated: InventoryInboundDraftRow = {
      ...matching,
      quantity: Number((matching.quantity + 1).toFixed(3)),
    };
    await this.saveDraftRow(updated);
    return updated;
  }

  private async persistRows(shopId: string, rows: readonly InventoryInboundDraftRow[]): Promise<void> {
    if (!shopId) {
      return;
    }
    if (rows.length === 0) {
      await this.draftStorage.clearRows(shopId);
    } else {
      await this.draftStorage.saveRows(shopId, rows);
    }
  }
}
