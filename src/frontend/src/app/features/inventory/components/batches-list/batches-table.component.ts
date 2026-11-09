import { BatchDispositionDialogComponent } from '../../../expiry/components/batch-disposition-dialog.component';
import { expiryStatusKey } from '../../../expiry/services/expiry-policy-evaluator';
import { CommonModule, DecimalPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { CheckboxModule } from 'primeng/checkbox';
import { MenuItem } from 'primeng/api';
import { Menu, MenuModule } from 'primeng/menu';
import { TagModule } from 'primeng/tag';
import { TableModule } from 'primeng/table';

import type { InventoryBatchDto } from '../../services/inventory.models';

export interface BatchTableAction {
  action: 'edit' | 'adjust' | 'count' | 'correctCost' | 'void' | 'printLabels' | 'refresh';
  batchId: string;
}

@Component({
  selector: 'app-batches-table',
  standalone: true,
  imports: [
    CommonModule,
    BatchDispositionDialogComponent,
    RouterLink,
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
  dispositionBatch: InventoryBatchDto | null = null;
  readonly expiryStatusKey = expiryStatusKey;
  private readonly translocoService = inject(TranslocoService);
  @Input({ required: true }) batches: InventoryBatchDto[] = [];
  @Input() loading = false;
  /** Receipt cost corrections move the supplier payable, so only an owner is offered them. */
  @Input() canCorrectReceiptCost = false;
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
  rowMenuItems: MenuItem[] = [];
  private pendingRowMenu: { menu: Menu; target: EventTarget | null } | null = null;

  openRowActions(event: MouseEvent, menu: Menu, batch: InventoryBatchDto): void {
    event.stopPropagation();
    // Keep item identities stable while focus changes between pointer down and click.
    this.rowMenuItems = this.rowActionItems(batch);
    if (this.pendingRowMenu || (menu.visible && menu.target !== event.currentTarget)) {
      this.pendingRowMenu = { menu, target: event.currentTarget };
      menu.hide();
      return;
    }
    menu.toggle(event);
  }

  onRowMenuHidden(): void {
    const pending = this.pendingRowMenu;
    this.pendingRowMenu = null;
    // Reopen only after the old overlay leaves, so PrimeNG positions at the new trigger.
    pending?.menu.show({ currentTarget: pending.target });
  }

  selectedReceiptLots(): InventoryBatchDto[] {
    return this.batches.filter((x) => this.selectedBatchIdSet.has(x.id) && !!x.receiptValueId);
  }
  selectedSupplierLots(): boolean {
    const lots = this.selectedReceiptLots();
    return (
      lots.length > 0 &&
      lots.every((x) => x.supplierId === lots[0].supplierId) &&
      !!lots[0].supplierId
    );
  }
  selectedReceiptIds(): string {
    return this.selectedReceiptLots()
      .map((x) => x.receiptValueId)
      .join(',');
  }
  onRowAction(action: BatchTableAction['action'], batchId: string): void {
    this.batchAction.emit({ action, batchId });
  }

  rowActionItems(batch: InventoryBatchDto): MenuItem[] {
    return [
      this.rowActionItem('printLabels', 'inventory.printLabels', 'pi pi-print', batch),
      this.rowActionItem('adjust', 'inventory.adjustBatch', 'pi pi-sliders-h', batch),
      this.rowActionItem('count', 'inventory.countStock', 'pi pi-calculator', batch),
      this.rowActionItem('edit', 'inventory.editPricesAndDetails', 'pi pi-pencil', batch),
      ...(this.canCorrectReceiptCost && !batch.isVoided
        ? [
            this.rowActionItem(
              'correctCost',
              'inventory.correctReceiptCost',
              'pi pi-receipt',
              batch,
            ),
          ]
        : []),
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
