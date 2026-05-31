import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InputNumberModule } from 'primeng/inputnumber';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { TranslocoPipe } from '@ngneat/transloco';
import type { BarcodeLabelPrintRequest } from '../services/inventory.models';

export interface BarcodeLabelPrintCandidate {
  readonly itemId: string;
  readonly itemName: string;
  readonly barcode: string;
  readonly inventoryBatchId: string | null;
  readonly quantity?: number;
}

interface BarcodeLabelPrintDialogRow extends BarcodeLabelPrintCandidate {
  readonly quantity: number;
}

export const BARCODE_LABEL_MAX_TOTAL_QUANTITY = 500;

@Component({
  selector: 'app-barcode-label-print-dialog',
  standalone: true,
  imports: [FormsModule, InputNumberModule, DialogModule, ButtonModule, TranslocoPipe],
  templateUrl: './barcode-label-print-dialog.component.html',
  styleUrl: './barcode-label-print-dialog.component.scss',
})
export class BarcodeLabelPrintDialogComponent {
  @Input() visible = false;

  readonly rows = signal<readonly BarcodeLabelPrintDialogRow[]>([]);

  @Input()
  set items(value: readonly BarcodeLabelPrintCandidate[]) {
    this.rows.set((value ?? []).map((item) => ({
      ...item,
      quantity: Number.isInteger(item.quantity) && (item.quantity ?? 0) > 0 ? Number(item.quantity) : 1,
    })));
  }

  @Output() readonly visibleChange = new EventEmitter<boolean>();
  @Output() readonly printRequested = new EventEmitter<BarcodeLabelPrintRequest>();
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly validationMessage = signal('');

  get canPrint(): boolean {
    return this.validationMessage().length === 0 && this.rows().length > 0;
  }

  onVisibleChange(visible: boolean): void {
    this.visible = visible;
    this.visibleChange.emit(visible);

    if (!visible) {
      this.closeRequested.emit();
      this.validationMessage.set('');
    }
  }

  onClose(): void {
    this.onVisibleChange(false);
  }

  onQuantityChange(index: number, value: number | null | undefined): void {
    const normalizedValue = Number(value ?? NaN);
    const nextQuantity = Number.isFinite(normalizedValue) ? normalizedValue : NaN;

    this.rows.update((currentRows) => {
      return currentRows.map((row, rowIndex) =>
        rowIndex === index ? { ...row, quantity: nextQuantity } : row,
      );
    });

    this.syncValidation();
  }

  onPrint(): void {
    this.syncValidation();
    if (!this.canPrint) {
      return;
    }

    this.printRequested.emit({
      items: this.rows().map((row) => ({
        itemId: row.itemId,
        quantity: row.quantity,
        inventoryBatchId: row.inventoryBatchId,
      })),
    });
  }

  getTotalQuantity(): number {
    return this.rows().reduce((total, row) => {
      return total + (Number.isInteger(row.quantity) ? row.quantity : 0);
    }, 0);
  }

  isRowQuantityValid(row: BarcodeLabelPrintDialogRow): boolean {
    return Number.isInteger(row.quantity) && row.quantity > 0;
  }

  getValidationKey(): string {
    if (this.rows().length === 0) {
      return 'inventory.barcodeLabels.validation.noItems';
    }

    const hasInvalidQuantity = this.rows().some((row) => !this.isRowQuantityValid(row));
    if (hasInvalidQuantity) {
      return 'inventory.barcodeLabels.validation.invalidQuantity';
    }

    if (this.getTotalQuantity() > BARCODE_LABEL_MAX_TOTAL_QUANTITY) {
      return 'inventory.barcodeLabels.validation.totalLimitExceeded';
    }

    return '';
  }

  private syncValidation(): void {
    this.validationMessage.set(this.getValidationKey());
  }
}
