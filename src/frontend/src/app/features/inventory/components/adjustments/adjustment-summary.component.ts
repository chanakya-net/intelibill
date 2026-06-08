import { DecimalPipe } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import {
  AdjustmentRowDto,
  InventoryAdjustmentDirection,
} from '../../services/inventory.models';

@Component({
  selector: 'app-adjustment-summary',
  standalone: true,
  imports: [DecimalPipe, TranslocoPipe, ProgressSpinnerModule],
  templateUrl: './adjustment-summary.component.html',
  styleUrl: './adjustment-summary.component.scss',
})
export class AdjustmentSummaryComponent {
  @Input() rows: AdjustmentRowDto[] = [];
  @Input() loading = false;

  constructor(private readonly translocoService: TranslocoService) {}

  get totalRows(): number {
    return this.rows.length;
  }

  get increaseQuantity(): number {
    return this.rows
      .filter((row) => row.direction === 'Increase')
      .reduce((sum, row) => sum + row.quantity, 0);
  }

  get decreaseQuantity(): number {
    return this.rows
      .filter((row) => row.direction === 'Decrease')
      .reduce((sum, row) => sum + row.quantity, 0);
  }

  get netQuantity(): number {
    return this.increaseQuantity - this.decreaseQuantity;
  }

  get netMovementLabel(): string {
    if (this.increaseQuantity === 0 && this.decreaseQuantity === 0) {
      return this.translocoService.translate('inventory.summary.noNetMovement');
    }

    const prefix = this.netQuantity > 0 ? '+' : '';
    return `${prefix}${this.netQuantity.toFixed(2)}`;
  }

  get reasonLabel(): string {
    if (this.totalRows === 1) {
      return this.translocoService.translate('inventory.summary.oneAdjustment');
    }

    return this.translocoService.translate('inventory.summary.multipleAdjustments', {
      count: this.totalRows,
    });
  }

  directionSeverity(direction: InventoryAdjustmentDirection): 'success' | 'danger' {
    return direction === 'Increase' ? 'success' : 'danger';
  }
}
