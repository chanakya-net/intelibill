import { Injectable, inject, signal } from '@angular/core';

import {
  InventoryDraftIndexedDbService,
  InventoryInboundDraftRow,
  InventoryInboundSubmission,
} from '../../../core/storage/inventory-draft-indexeddb.service';

/** The server's per-request row limit. */
export const INBOUND_BATCH_CHUNK_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class BatchDraftStateService {
  private readonly draftStorage = inject(InventoryDraftIndexedDbService);

  readonly pendingRows = signal<readonly InventoryInboundDraftRow[]>([]);
  readonly loadingDraft = signal(false);

  /** True while a save whose outcome is unknown waits to be resent unchanged. */
  readonly hasInterruptedSubmission = signal(false);

  async loadDraftRows(shopId: string): Promise<readonly InventoryInboundDraftRow[]> {
    this.loadingDraft.set(true);

    try {
      const rows = await this.draftStorage.loadRows(shopId);
      this.pendingRows.set(rows);
      this.hasInterruptedSubmission.set((await this.draftStorage.loadSubmission(shopId)) !== null);
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

  /**
   * The submission to send: the interrupted one when it exists, otherwise the given rows frozen
   * into chunks — `performedAt` stamped once — and stored before anything is sent. Rejects when
   * the freeze cannot be stored, in which case nothing may be sent.
   */
  async prepareSubmission(
    shopId: string,
    rows: readonly InventoryInboundDraftRow[],
  ): Promise<InventoryInboundSubmission> {
    const interrupted = await this.draftStorage.loadSubmission(shopId);
    if (interrupted) {
      // Possibly stored by another tab; it is what will be sent, so the page must treat it as such.
      this.hasInterruptedSubmission.set(true);
      return interrupted;
    }

    const submissionId = crypto.randomUUID();
    const performedAt = new Date().toISOString();
    const frozenRows = rows.map((row) => ({ ...row, performedAt: row.performedAt ?? performedAt }));
    const chunks = [];
    for (let index = 0; index < frozenRows.length; index += INBOUND_BATCH_CHUNK_SIZE) {
      chunks.push({
        operationKey: `intake-${submissionId}-${index / INBOUND_BATCH_CHUNK_SIZE}`,
        rows: frozenRows.slice(index, index + INBOUND_BATCH_CHUNK_SIZE),
      });
    }

    const submission: InventoryInboundSubmission = { shopId, chunks, createdAt: performedAt };
    await this.draftStorage.saveSubmission(submission);
    return submission;
  }

  markSubmissionInterrupted(): void {
    this.hasInterruptedSubmission.set(true);
  }

  async clearSubmission(shopId: string): Promise<void> {
    this.hasInterruptedSubmission.set(false);
    await this.draftStorage.clearSubmission(shopId);
  }
}
