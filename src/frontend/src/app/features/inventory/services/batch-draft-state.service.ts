import { Injectable, computed, inject, signal } from '@angular/core';
import type { ScopeOwner } from '../../../core/context/shop-scope';

import {
  InventoryDraftIndexedDbService,
  InventoryInboundDraftRow,
  InventoryInboundSubmission,
  sameIntakeSubmission,
} from '../../../core/storage/inventory-draft-indexeddb.service';

/** The server's per-request row limit. */
export const INBOUND_BATCH_CHUNK_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class BatchDraftStateService {
  private readonly draftStorage = inject(InventoryDraftIndexedDbService);

  readonly pendingRows = signal<readonly InventoryInboundDraftRow[]>([]);
  readonly loadingDraft = signal(false);

  private readonly displayedSubmission = signal<InventoryInboundSubmission | null>(null);
  readonly hasInterruptedSubmission = computed(() => this.displayedSubmission() !== null);

  reset(): void {
    this.pendingRows.set([]);
    this.displayedSubmission.set(null);
    this.loadingDraft.set(false);
  }

  async loadDraftRows(shopId: string, owner?: ScopeOwner, current = () => true): Promise<readonly InventoryInboundDraftRow[]> {
    this.loadingDraft.set(true);
    try {
      const draft = await this.draftStorage.loadDraft(shopId, owner);
      if (!current()) return [];
      this.pendingRows.set(draft.rows);
      this.displayedSubmission.set(draft.submission);
      return draft.rows;
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

  async prepareSubmission(owner: ScopeOwner, assertScope: () => void): Promise<InventoryInboundSubmission | null> {
    const displayed = this.displayedSubmission();
    if (displayed) {
      const current = await this.draftStorage.loadDraft(owner.shopId, owner);
      assertScope();
      if (current.submission && sameIntakeSubmission(displayed, current.submission)) return displayed;
      this.pendingRows.set(current.rows);
      this.displayedSubmission.set(current.submission);
      return null;
    }

    const rows = this.pendingRows();
    const saved = await this.draftStorage.saveSubmission(owner, rows, () => this.freezeSubmission(owner, rows), assertScope);
    assertScope();
    if (saved) return saved;
    await this.refreshAfterConcurrentUpdate(owner, assertScope);
    return null;
  }

  private freezeSubmission(owner: ScopeOwner, rows: readonly InventoryInboundDraftRow[]) {
    const submissionId = crypto.randomUUID();
    const performedAt = new Date().toISOString();
    const frozenRows = rows.map(row => ({ ...row, performedAt: row.performedAt ?? performedAt }));
    const chunks = [];
    for (let index = 0; index < frozenRows.length; index += INBOUND_BATCH_CHUNK_SIZE) {
      chunks.push({
        operationKey: `intake-${submissionId}-${index / INBOUND_BATCH_CHUNK_SIZE}`,
        rows: frozenRows.slice(index, index + INBOUND_BATCH_CHUNK_SIZE),
      });
    }
    return { shopId: owner.shopId, ownerUserId: owner.userId, chunks, createdAt: performedAt };
  }

  markSubmissionInterrupted(submission: InventoryInboundSubmission): void {
    this.displayedSubmission.set(submission);
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
    this.displayedSubmission.set(complete ? null : expected);
    return true;
  }

  private async refreshAfterConcurrentUpdate(owner: ScopeOwner, assertScope: () => void): Promise<void> {
    await this.loadDraftRows(owner.shopId, owner, () => {
      assertScope();
      return true;
    });
  }
}
