import { CommonModule, DecimalPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { CheckboxModule } from 'primeng/checkbox';
import { MenuItem } from 'primeng/api';
import { MenuModule } from 'primeng/menu';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import type { InventoryBatchDto } from '../../services/inventory.models';

export interface BatchTableAction {
  action: 'edit' | 'adjust' | 'void' | 'printLabels';
  batchId: string;
}

@Component({
  selector: 'app-batches-table',
  standalone: true,
  imports: [
    CommonModule,
    DecimalPipe,
    FormsModule,
    AvatarModule,
    ButtonModule,
    CardModule,
    CheckboxModule,
    MenuModule,
    TagModule,
    TableModule,
    TranslocoPipe,
  ],
  templateUrl: './batches-table.component.html',
  styleUrl: './batches-table.component.scss',
})
export class BatchesTableComponent {
  private readonly translocoService = inject(TranslocoService);
  @Input({ required: true }) batches: InventoryBatchDto[] = [];
  @Input() loading = false;
  @Input() set selectedBatchIds(value: readonly string[] | null | undefined) {
    this._selectedBatchIds = value ?? [];
    this.selectedBatchIdSet = new Set(this._selectedBatchIds);
  }
  get selectedBatchIds(): readonly string[] {
    return this._selectedBatchIds;
  }
  @Output() batchClicked = new EventEmitter<string>();
  @Output() batchAction = new EventEmitter<BatchTableAction>();
  @Output() selectionChange = new EventEmitter<readonly string[]>();

  private _selectedBatchIds: readonly string[] = [];
  private selectedBatchIdSet = new Set<string>();

  onRowAction(action: 'edit' | 'adjust' | 'void' | 'printLabels', batchId: string): void {
    this.batchAction.emit({ action, batchId });
  }

  rowActionItems(batch: InventoryBatchDto): MenuItem[] {
    return [
      this.rowActionItem('printLabels', 'inventory.printLabels', 'pi pi-print', batch),
      this.rowActionItem('adjust', 'inventory.adjustBatch', 'pi pi-sliders-h', batch),
      this.rowActionItem('edit', 'common.edit', 'pi pi-pencil', batch),
      this.rowActionItem('void', 'common.void', 'pi pi-trash', batch),
    ];
  }

  onRowSelectionChange(batch: InventoryBatchDto, selected: boolean): void {
    if (batch.isVoided) {
      return;
    }

    const nextSelection = new Set(this.selectedBatchIds);
    if (selected) {
      nextSelection.add(batch.id);
    } else {
      nextSelection.delete(batch.id);
    }

    this.selectionChange.emit([...nextSelection]);
  }

  onSelectAllChange(selected: boolean): void {
    const selectableIds = this.batches.filter((batch) => !batch.isVoided).map((batch) => batch.id);
    this.selectionChange.emit(selected ? selectableIds : []);
  }

  isBatchSelected(batchId: string): boolean {
    return this.selectedBatchIdSet.has(batchId);
  }

  isAllSelectableSelected(): boolean {
    const selectableIds = this.batches.filter((batch) => !batch.isVoided).map((batch) => batch.id);
    return (
      selectableIds.length > 0 &&
      selectableIds.every((batchId) => this.selectedBatchIdSet.has(batchId))
    );
  }

  hasSelectableBatches(): boolean {
    return this.batches.some((batch) => !batch.isVoided);
  }

  onRowSelect(batch: InventoryBatchDto | null | undefined): void {
    if (!batch || batch.isVoided) {
      return;
    }

    this.batchClicked.emit(batch.id);
  }

  productInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].slice(0, 2).toUpperCase();

    return (words[0][0] + words[1][0]).toUpperCase();
  }

  productAvatarColor(name: string): string {
    const colors = [
      '#b45309',
      '#0369a1',
      '#15803d',
      '#7c3aed',
      '#be185d',
      '#c2410c',
      '#0f766e',
      '#1d4ed8',
    ];

    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }

    return colors[Math.abs(hash) % colors.length];
  }

  private rowActionItem(
    action: BatchTableAction['action'],
    labelKey: string,
    icon: string,
    batch: InventoryBatchDto,
  ): MenuItem {
    return {
      id: action,
      label: this.translocoService.translate(labelKey),
      icon,
      disabled: batch.isVoided,
      command: () => this.onRowAction(action, batch.id),
    };
  }
}
