import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import {
  InventoryAdjustmentDirection,
  InventoryAdjustmentHistoryItem,
  InventoryAdjustmentReason,
} from '../../services/inventory.models';

interface ReasonOption {
  readonly label: string;
  readonly value: InventoryAdjustmentReason;
}

@Component({
  selector: 'app-adjustment-summary',
  standalone: true,
  imports: [DatePipe, DecimalPipe, TranslocoPipe, ButtonModule, CardModule, TableModule, TagModule],
  templateUrl: './adjustment-summary.component.html',
})
export class AdjustmentSummaryComponent {
  private readonly translocoService = inject(TranslocoService);

  @Input() rows: InventoryAdjustmentHistoryItem[] = [];
  @Input() loading = false;
  @Input() canVoidAdjustments = false;
  @Output() readonly voidRequested = new EventEmitter<InventoryAdjustmentHistoryItem>();

  private readonly allReasonOptions: ReasonOption[] = [
    { label: this.translate('inventory.adjustmentReason.damaged'), value: 'Damaged' },
    { label: this.translate('inventory.adjustmentReason.expired'), value: 'Expired' },
    { label: this.translate('inventory.adjustmentReason.stolen'), value: 'Stolen' },
    { label: this.translate('inventory.adjustmentReason.missingLost'), value: 'MissingLost' },
    {
      label: this.translate('inventory.adjustmentReason.stockCountCorrection'),
      value: 'StockCountCorrection',
    },
    { label: this.translate('inventory.adjustmentReason.otherLoss'), value: 'OtherLoss' },
    { label: this.translate('inventory.adjustmentReason.foundStock'), value: 'FoundStock' },
    {
      label: this.translate('inventory.adjustmentReason.returnRestockCorrection'),
      value: 'ReturnRestockCorrection',
    },
    { label: this.translate('inventory.adjustmentReason.otherGain'), value: 'OtherGain' },
  ];

  reasonLabel(reason: InventoryAdjustmentReason): string {
    return this.allReasonOptions.find((opt) => opt.value === reason)?.label ?? reason;
  }

  directionSeverity(direction: InventoryAdjustmentDirection): 'success' | 'danger' {
    return direction === 'Increase' ? 'success' : 'danger';
  }

  statusSeverity(adjustment: InventoryAdjustmentHistoryItem): 'success' | 'danger' {
    return adjustment.isVoided ? 'danger' : 'success';
  }

  canVoidAdjustment(adjustment: InventoryAdjustmentHistoryItem): boolean {
    return this.canVoidAdjustments && !adjustment.isVoided;
  }

  onVoidRequested(adjustment: InventoryAdjustmentHistoryItem): void {
    if (!this.canVoidAdjustment(adjustment)) return;
    this.voidRequested.emit(adjustment);
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }
}
