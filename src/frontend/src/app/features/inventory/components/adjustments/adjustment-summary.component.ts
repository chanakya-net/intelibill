import { DecimalPipe } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { CardModule } from 'primeng/card';

import {
  AdjustmentRowDto,
  InventoryAdjustmentDirection,
} from '../../services/inventory.models';

@Component({
  selector: 'app-adjustment-summary',
  standalone: true,
  imports: [CardModule, DecimalPipe, TranslocoPipe],
  templateUrl: './adjustment-summary.component.html',
})
export class AdjustmentSummaryComponent {
  @Input() rows: AdjustmentRowDto[] = [];
  @Input() loading = false;

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

  get reasonLabel(): string {
    return this.totalRows === 1 ? '1 adjustment' : `${this.totalRows} adjustments`;
  }

  get directionLabel(): string {
    if (this.increaseQuantity === 0 && this.decreaseQuantity === 0) return 'No net movement';
    return `${this.increaseQuantity > 0 ? '+' : ''}${this.netQuantity.toFixed(2)}`;
  }

  directionSeverity(direction: InventoryAdjustmentDirection): 'success' | 'danger' {
    return direction === 'Increase' ? 'success' : 'danger';
  }
}
