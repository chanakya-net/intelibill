import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { TranslocoPipe } from '@ngneat/transloco';

export interface CreateSaleResponse {
  readonly saleId: string;
  readonly invoiceNumber: string;
  readonly totalAmount: number;
  readonly creditNoteAppliedAmount?: number;
  readonly creditNoteRedemptionSummaries?: readonly {
    readonly creditNoteId: string;
    readonly code: string;
    readonly amount: number;
  }[];
  readonly printTemplate?: 'a4' | 'thermal';
  readonly isOffline?: boolean;
}

@Component({
  selector: 'app-sale-confirmation-dialog',
  standalone: true,
  imports: [CommonModule, DialogModule, ButtonModule, TranslocoPipe],
  templateUrl: './sale-confirmation-dialog.component.html',
})
export class SaleConfirmationDialogComponent {
  @Input() visible = false;
  @Input() saleResult: CreateSaleResponse | null = null;
  @Input() titleKey = 'sales.newSale.confirmation.title';
  @Input() saleTypeLabel = 'sales.invoice.invoiceNumber';
  @Input() doneButtonLabel = 'sales.invoice.done';
  @Input() printA4ButtonLabel = 'sales.invoice.printA4';
  @Input() printThermalButtonLabel = 'sales.invoice.printThermal';

  @Output() closed = new EventEmitter<void>();
  @Output() printA4Requested = new EventEmitter<void>();
  @Output() printThermalRequested = new EventEmitter<void>();

  onClose(): void {
    this.closed.emit();
  }

  onPrintA4(): void {
    this.printA4Requested.emit();
  }

  onPrintThermal(): void {
    this.printThermalRequested.emit();
  }
}
