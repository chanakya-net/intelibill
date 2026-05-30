import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { DateOnlyPipe } from '../../../../shared/pipes/date-only.pipe';
import { TranslocoPipe } from '@ngneat/transloco';
import type { SellableDto, SellableGoodsDto, SellableServiceDto } from '../../../sales/services/sale.models';

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
  @Input() batches: readonly SellableDto[] = [];
  @Input() loading = false;
  @Input() quantity = 1;

  @Output() batchSelected = new EventEmitter<SellableDto>();
  @Output() closed = new EventEmitter<void>();
  @Output() quantityChanged = new EventEmitter<number>();

  selectedBatch: SellableDto | null = null;

  isGoods(sellable: SellableDto): sellable is SellableGoodsDto {
    return sellable.kind === 'Goods';
  }

  isService(sellable: SellableDto): sellable is SellableServiceDto {
    return sellable.kind === 'Service';
  }

  onSelectBatch(batch: SellableDto): void {
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

  getDisplayName(sellable: SellableDto): string {
    return sellable.kind === 'Goods' ? sellable.itemName : sellable.name;
  }

  getDisplayPrice(sellable: SellableDto): number {
    return sellable.kind === 'Goods' ? sellable.salesPrice : sellable.price;
  }

  getDisplayId(sellable: SellableDto): string {
    return sellable.kind === 'Goods' ? sellable.batchNumber : sellable.code;
  }
}
