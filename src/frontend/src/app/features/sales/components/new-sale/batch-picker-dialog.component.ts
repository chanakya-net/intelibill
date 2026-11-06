import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, viewChild } from '@angular/core';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { DateOnlyPipe } from '../../../../shared/pipes/date-only.pipe';
import { QuantityFieldComponent } from '../../../../shared/quantity/quantity-field.component';
import { TranslocoPipe } from '@ngneat/transloco';
import type { SellableDto, SellableGoodsDto, SellableServiceDto } from '../../../sales/services/sale.models';

@Component({
  selector: 'app-batch-picker-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    DialogModule,
    DateOnlyPipe,
    QuantityFieldComponent,
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

  private readonly quantityField = viewChild(QuantityFieldComponent);

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

    // Whatever is typed must stand as this item's quantity before anything is added; the field says why if not.
    if (this.quantityField()?.commit() === false) {
      return;
    }

    this.batchSelected.emit(this.selectedBatch);
  }

  onClose(): void {
    this.selectedBatch = null;
    this.closed.emit();
  }

  /** The quantity field committed a valid quantity for the selected sellable's unit. */
  onQuantityChanged(value: number): void {
    this.quantity = value;
    this.quantityChanged.emit(value);
  }

  /** Decimal places the selected sellable's quantity may carry; service lines are whole units in the cart. */
  quantityScaleOf(sellable: SellableDto): number {
    return sellable.kind === 'Goods' ? (sellable.quantityScale ?? 0) : 0;
  }

  unitLabelOf(sellable: SellableDto): string {
    return sellable.kind === 'Goods' ? (sellable.uom ?? '') : '';
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
