import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { DateOnlyPipe } from '../../../../shared/pipes/date-only.pipe';
import { TranslocoPipe } from '@ngneat/transloco';
import { AvailableBatchDto } from '../../../inventory/services/inventory.models';

@Component({
  selector: 'app-batch-picker-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    InputNumberModule,
    DateOnlyPipe,
    TranslocoPipe,
  ],
  templateUrl: './batch-picker-dialog.component.html',
  styleUrl: './batch-picker-dialog.component.scss',
})
export class BatchPickerDialogComponent {
  @Input() visible = false;
  @Input() batches: readonly AvailableBatchDto[] = [];
  @Input() loading = false;
  @Input() quantity = 1;

  @Output() batchSelected = new EventEmitter<AvailableBatchDto>();
  @Output() closed = new EventEmitter<void>();
  @Output() quantityChanged = new EventEmitter<number>();

  selectedBatch: AvailableBatchDto | null = null;

  onSelectBatch(batch: AvailableBatchDto): void {
    this.selectedBatch = batch;
  }

  onAddToCart(): void {
    if (!this.selectedBatch) {
      return;
    }

    this.batchSelected.emit(this.selectedBatch);
  }

  onClose(): void {
    this.selectedBatch = null;
    this.closed.emit();
  }

  onQuantityChanged(value: number | null): void {
    const qty = Number(value ?? 1);
    this.quantity = Number.isFinite(qty) ? qty : 1;
    this.quantityChanged.emit(this.quantity);
  }
}
