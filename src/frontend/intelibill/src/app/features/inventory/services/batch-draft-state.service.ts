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

  /** The rows this tab last read from or wrote to the device: what a change must still find there. */
  private storedRows: readonly InventoryInboundDraftRow[] = [];
  /** Moves whenever the view is replaced from storage, so a change queued before it is dropped. */
  private viewVersion = 0;
  private writes: Promise<unknown> = Promise.resolve();

  reset(): void {
    this.show([], null);
    this.loadingDraft.set(false);
  }

  async loadDraftRows(shopId: string, owner?: ScopeOwner, current = () => true): Promise<readonly InventoryInboundDraftRow[]> {
    this.loadingDraft.set(true);
    try {
      const draft = await this.draftStorage.loadDraft(shopId, owner);
      if (!current()) return [];
      this.show(draft.rows, draft.submission);
      return draft.rows;
    } finally {
      if (current()) this.loadingDraft.set(false);
    }
  }

  /** Resolves false when another tab changed the draft first; the view then shows the stored draft. */
  savePendingRows(
    shopId: string,
    rows: readonly InventoryInboundDraftRow[],
    owner?: ScopeOwner,
  ): Promise<boolean> {
    return this.change(shopId, owner, () => rows);
  }

  /** A new row is kept after rows another tab changed, unless a save has frozen them. */
  async addRow(shopId: string, row: InventoryInboundDraftRow, owner?: ScopeOwner): Promise<boolean> {
    const append = (rows: readonly InventoryInboundDraftRow[]) => [...rows, row];
    return await this.change(shopId, owner, append) ||
      (!this.hasInterruptedSubmission() && await this.change(shopId, owner, append));
  }

  removeRow(shopId: string, clientRowId: string, owner?: ScopeOwner): Promise<boolean> {
    return this.change(shopId, owner, rows => rows.filter((row) => row.clientRowId !== clientRowId));
  }

  clearRows(shopId: string, owner?: ScopeOwner): Promise<boolean> {
    return this.change(shopId, owner, () => []);
  }

  async updateRow(
    shopId: string,
    clientRowId: string,
    updater: (row: InventoryInboundDraftRow) => InventoryInboundDraftRow,
  ): Promise<InventoryInboundDraftRow | null> {
    const currentRow = this.pendingRows().find((row) => row.clientRowId === clientRowId);
    if (!currentRow) {
      return null;
    }

    const nextRow = updater(currentRow);
    const saved = await this.change(shopId, undefined,
      rows => rows.map((row) => (row.clientRowId === clientRowId ? nextRow : row)));
    return saved ? nextRow : null;
  }

  /** Null when no pending row has this barcode, including after another tab's change removed it. */
  async incrementRowQuantity(shopId: string, barcode: string, owner?: ScopeOwner): Promise<InventoryInboundDraftRow | null> {
    for (let attempt = 0; attempt < 2; attempt++) {
      const currentRow = this.pendingRows().find((row) => row.barcode === barcode);
      if (!currentRow) return null;

      const nextRow = {
        ...currentRow,
        quantity: Number((currentRow.quantity + 1).toFixed(3)),
      } satisfies InventoryInboundDraftRow;
      const saved = await this.change(shopId, owner,
        rows => rows.map((row) => (row.clientRowId === currentRow.clientRowId ? nextRow : row)));
      if (saved) return nextRow;
      if (this.hasInterruptedSubmission()) return null;
    }
    return null;
  }

  /**
   * Shows the change at once and stores it only if the device still holds the rows this tab knows.
   * Writes run one at a time, each against the rows the previous one left.
   */
  private change(
    shopId: string,
    owner: ScopeOwner | undefined,
    next: (rows: readonly InventoryInboundDraftRow[]) => readonly InventoryInboundDraftRow[],
  ): Promise<boolean> {
    const rows = next(this.pendingRows());
    const version = this.viewVersion;
    this.pendingRows.set(rows);
    if (!shopId) return Promise.resolve(true);

    const write = async (): Promise<boolean> => {
      if (version !== this.viewVersion) return false;
      const refusedBy = await (owner
        ? this.draftStorage.saveRows(shopId, rows, this.storedRows, owner)
        : this.draftStorage.saveRows(shopId, rows, this.storedRows));
      if (version !== this.viewVersion) return false;
      if (refusedBy) {
        this.show(refusedBy.rows, refusedBy.submission);
        return false;
      }
      this.storedRows = rows;
      return true;
    };
    const result = this.writes.then(write, write);
    this.writes = result.catch(() => undefined);
    return result;
  }

  private show(rows: readonly InventoryInboundDraftRow[], submission: InventoryInboundSubmission | null): void {
    this.viewVersion++;
    this.storedRows = rows;
    this.pendingRows.set(rows);
    this.displayedSubmission.set(submission);
  }

  async prepareSubmission(owner: ScopeOwner, assertScope: () => void): Promise<InventoryInboundSubmission | null> {
    const displayed = this.displayedSubmission();
    if (displayed) {
      const current = await this.draftStorage.loadDraft(owner.shopId, owner);
      assertScope();
      if (current.submission && sameIntakeSubmission(displayed, current.submission)) return displayed;
      this.show(current.rows, current.submission);
      return null;
    }

    // A row change still on its way to the device belongs to the draft being frozen.
    await this.writes;
    assertScope();
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
    this.show(rows, complete ? null : expected);
    return true;
  }

  private async refreshAfterConcurrentUpdate(owner: ScopeOwner, assertScope: () => void): Promise<void> {
    await this.loadDraftRows(owner.shopId, owner, () => {
      assertScope();
      return true;
    });
  }
}
