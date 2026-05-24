import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { BatchDraftStateService } from '../../services/batch-draft-state.service';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import type { AddInventoryBatchResponse } from '../../services/inventory.models';
import { InventoryInboundDraftRow } from '../../../../core/storage/inventory-draft-indexeddb.service';

@Component({
  selector: 'app-batch-save-results',
  standalone: true,
  imports: [TranslocoPipe, ButtonModule, ProgressSpinnerModule, TableModule],
  templateUrl: './batch-save-results.component.html',
})
export class BatchSaveResultsComponent {
  private readonly draftState = inject(BatchDraftStateService);
  private readonly suppliersFacade = inject(SuppliersFacade);

  @Input() saveSummary: AddInventoryBatchResponse | null = null;
  @Input() isSaving = false;
  @Input() highlightedRowId: string | null = null;

  @Output() readonly editRow = new EventEmitter<string>();
  @Output() readonly removeRow = new EventEmitter<string>();
  @Output() readonly clearAll = new EventEmitter<void>();
  @Output() readonly saveAll = new EventEmitter<void>();

  readonly suppliers = this.suppliersFacade.suppliers;

  failedClientRowIds(): Set<string> {
    if (!this.saveSummary) {
      return new Set<string>();
    }

    return new Set(this.saveSummary.failed.map((row) => row.clientRowId));
  }

  failedRowErrorTextById(): Map<string, string> {
    if (!this.saveSummary) {
      return new Map<string, string>();
    }

    const entries = this.saveSummary.failed.map((row) => [
      row.clientRowId,
      row.errors.map((error) => error.description).join(', '),
    ] as const);

    return new Map<string, string>(entries);
  }

  pendingRowInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) {
      return words[0].substring(0, 2).toUpperCase();
    }

    return (words[0][0] + words[1][0]).toUpperCase();
  }

  pendingRowAvatarColor(name: string): string {
    const colors = ['#b45309', '#0369a1', '#15803d', '#7c3aed', '#be185d', '#c2410c', '#0f766e', '#1d4ed8'];
    let hash = 0;
    for (let index = 0; index < name.length; index += 1) {
      hash = name.charCodeAt(index) + ((hash << 5) - hash);
    }

    return colors[Math.abs(hash) % colors.length];
  }

  getSupplierDisplayName(supplierId: string | null): string {
    if (!supplierId) {
      return '-';
    }

    return this.suppliers().find((supplier) => supplier.supplierId === supplierId)?.name ?? supplierId;
  }

  pendingRows(): InventoryInboundDraftRow[] {
    return [...this.draftState.pendingRows()];
  }

  loadingDraft(): boolean {
    return this.draftState.loadingDraft();
  }
}
