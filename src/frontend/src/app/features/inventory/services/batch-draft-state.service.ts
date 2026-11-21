import { Injectable, inject, signal } from '@angular/core';
import type { ScopeOwner } from '../../../core/context/shop-scope';

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

  reset(): void {
    this.pendingRows.set([]);
    this.hasInterruptedSubmission.set(false);
    this.loadingDraft.set(false);
  }

  async loadDraftRows(shopId: string, owner?: ScopeOwner, current = () => true): Promise<readonly InventoryInboundDraftRow[]> {
    this.loadingDraft.set(true);
    try {
      const rows = await (owner ? this.draftStorage.loadRows(shopId, owner) : this.draftStorage.loadRows(shopId));
      const submission = await this.draftStorage.loadSubmission(shopId, owner);
      if (!current()) return [];
      this.pendingRows.set(rows);
      this.hasInterruptedSubmission.set(submission !== null);
      return rows;
    } finally {
      if (current()) this.loadingDraft.set(false);
    }
  }

  async savePendingRows(
    shopId: string,
    rows: readonly InventoryInboundDraftRow[],
    owner?: ScopeOwner,
  ): Promise<void> {
    this.pendingRows.set(rows);
    if (!shopId) {
      return;
    }

    if (rows.length === 0) {
      await (owner ? this.draftStorage.clearRows(shopId, owner) : this.draftStorage.clearRows(shopId));
      return;
    }

    await (owner ? this.draftStorage.saveRows(shopId, rows, owner) : this.draftStorage.saveRows(shopId, rows));
  }

  async addRow(shopId: string, row: InventoryInboundDraftRow, owner?: ScopeOwner): Promise<void> {
    await this.savePendingRows(shopId, [...this.pendingRows(), row], owner);
  }

  async removeRow(shopId: string, clientRowId: string, owner?: ScopeOwner): Promise<void> {
    const rows = this.pendingRows().filter((row) => row.clientRowId !== clientRowId);
    await this.savePendingRows(shopId, rows, owner);
  }

  async clearRows(shopId: string, owner?: ScopeOwner): Promise<void> {
    this.pendingRows.set([]);
    if (!shopId) {
      return;
    }

    await (owner ? this.draftStorage.clearRows(shopId, owner) : this.draftStorage.clearRows(shopId));
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

  async incrementRowQuantity(shopId: string, barcode: string, owner?: ScopeOwner): Promise<InventoryInboundDraftRow | null> {
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
    await this.savePendingRows(shopId, nextRows, owner);
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
    owner?: ScopeOwner,
    assertScope: () => void = () => undefined,
    allowRecovery = true,
  ): Promise<InventoryInboundSubmission | null> {
    const interrupted = await this.draftStorage.loadSubmission(shopId, owner);
    assertScope();
    if (interrupted) {
      // Possibly stored by another tab; it is what will be sent, so the page must treat it as such.
      this.hasInterruptedSubmission.set(true);
      return allowRecovery ? interrupted : null;
    }
    if (allowRecovery && owner) {
      // Another tab may have finished the save. A recovery click never creates a fresh key.
      await this.refreshAfterConcurrentUpdate(owner, assertScope);
      return null;
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

    const submission: InventoryInboundSubmission = { shopId, ownerUserId: owner?.userId, chunks, createdAt: performedAt };
    const selected = await this.draftStorage.saveSubmission(submission, assertScope);
    assertScope();
    if (selected !== submission && !allowRecovery) {
      this.hasInterruptedSubmission.set(true);
      return null;
    }
    return selected;
  }

  markSubmissionInterrupted(): void {
    this.hasInterruptedSubmission.set(true);
  }

  async settleSubmission(
    expected: InventoryInboundSubmission,
    savedIds: ReadonlySet<string>,
    complete: boolean,
    owner: ScopeOwner,
    assertScope: () => void,
  ): Promise<boolean> {
    const rows = await this.draftStorage.settleSubmission(expected, savedIds, complete, assertScope);
    assertScope();
    if (!rows) {
      await this.refreshAfterConcurrentUpdate(owner, assertScope);
      return false;
    }
    this.pendingRows.set(rows);
    this.hasInterruptedSubmission.set(!complete);
    return true;
  }

  private async refreshAfterConcurrentUpdate(owner: ScopeOwner, assertScope: () => void): Promise<void> {
    await this.loadDraftRows(owner.shopId, owner, () => {
      assertScope();
      return true;
    });
  }
}
