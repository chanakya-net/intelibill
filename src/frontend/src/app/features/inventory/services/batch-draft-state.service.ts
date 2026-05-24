import { Injectable, inject, signal } from '@angular/core';

import { InventoryDraftIndexedDbService } from '../../../core/storage/inventory-draft-indexeddb.service';
import { InventoryInboundDraftRow } from '../../../core/storage/inventory-draft-indexeddb.service';

@Injectable({ providedIn: 'root' })
export class BatchDraftStateService {
  private readonly draftStorage = inject(InventoryDraftIndexedDbService);

  readonly pendingRows = signal<readonly InventoryInboundDraftRow[]>([]);
  readonly loadingDraft = signal(false);

  async loadDraftRows(shopId: string): Promise<readonly InventoryInboundDraftRow[]> {
    this.loadingDraft.set(true);

    try {
      const rows = await this.draftStorage.loadRows(shopId);
      this.pendingRows.set(rows);
      return rows;
    } finally {
      this.loadingDraft.set(false);
    }
  }

  async savePendingRows(
    shopId: string,
    rows: readonly InventoryInboundDraftRow[],
  ): Promise<void> {
    this.pendingRows.set(rows);
    if (!shopId) {
      return;
    }

    if (rows.length === 0) {
      await this.draftStorage.clearRows(shopId);
      return;
    }

    await this.draftStorage.saveRows(shopId, rows);
  }

  async addRow(shopId: string, row: InventoryInboundDraftRow): Promise<void> {
    await this.savePendingRows(shopId, [...this.pendingRows(), row]);
  }

  async removeRow(shopId: string, clientRowId: string): Promise<void> {
    const rows = this.pendingRows().filter((row) => row.clientRowId !== clientRowId);
    await this.savePendingRows(shopId, rows);
  }

  async clearRows(shopId: string): Promise<void> {
    this.pendingRows.set([]);
    if (!shopId) {
      return;
    }

    await this.draftStorage.clearRows(shopId);
  }

  async updateRow(
    shopId: string,
    clientRowId: string,
    updater: (row: InventoryInboundDraftRow) => InventoryInboundDraftRow,
  ): Promise<InventoryInboundDraftRow | null> {
    const currentRows = this.pendingRows();
    const currentRow = currentRows.find((row) => row.clientRowId === clientRowId);
    if (!currentRow) {
      return null;
    }

    const nextRow = updater(currentRow);
    const nextRows = currentRows.map((row) => (row.clientRowId === clientRowId ? nextRow : row));
    await this.savePendingRows(shopId, nextRows);
    return nextRow;
  }

  async incrementRowQuantity(shopId: string, barcode: string): Promise<InventoryInboundDraftRow | null> {
    const currentRows = this.pendingRows();
    const currentRow = currentRows.find((row) => row.barcode === barcode);
    if (!currentRow) {
      return null;
    }

    const nextRow = {
      ...currentRow,
      quantity: Number((currentRow.quantity + 1).toFixed(3)),
    } satisfies InventoryInboundDraftRow;
    const nextRows = currentRows.map((row) => (row.clientRowId === currentRow.clientRowId ? nextRow : row));
    await this.savePendingRows(shopId, nextRows);
    return nextRow;
  }
}
