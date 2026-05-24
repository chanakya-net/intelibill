import { Component, EventEmitter, inject, Output } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { InventoryInboundDraftRow } from '../../../../core/storage/inventory-draft-indexeddb.service';
import { BatchHsnPickerDialogComponent } from './batch-hsn-picker-dialog.component';
import { BatchRowFormStateService } from '../../services/batch-row-form-state.service';

@Component({
  selector: 'app-batch-row-form',
  standalone: true,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslocoPipe,
    AutoCompleteModule,
    ButtonModule,
    InputGroupModule,
    InputGroupAddonModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TextareaModule,
    ProgressSpinnerModule,
    BatchHsnPickerDialogComponent,
  ],
  templateUrl: './batch-row-form.component.html',
})
export class BatchRowFormComponent {
  readonly state = inject(BatchRowFormStateService);
  readonly form = this.state.form;

  @Output() readonly rowSubmitted = new EventEmitter<InventoryInboundDraftRow>();
  @Output() readonly rowAdded = new EventEmitter<InventoryInboundDraftRow>();
  @Output() readonly scannerRequested = new EventEmitter<void>();
  @Output() readonly scanRequested = new EventEmitter<void>();

  submit(): void {
    this.tryAddRow();
  }

  tryAddRow(): boolean {
    const row = this.state.buildDraftRow();
    if (!row) {
      this.state.form.markAllAsTouched();
      return false;
    }

    this.rowSubmitted.emit(row);
    this.rowAdded.emit(row);
    this.state.resetForm();
    return true;
  }

  populateFromRow(row: InventoryInboundDraftRow): void {
    this.state.loadDraftRow(row);
  }

  resetForm(): void {
    this.state.resetForm();
  }

  async handleBarcode(barcode: string): Promise<'added' | 'review'> {
    const row = await this.state.prepareScannedRow(barcode);
    if (!row) {
      return 'review';
    }

    this.rowSubmitted.emit(row);
    this.rowAdded.emit(row);
    this.state.resetForm();
    return 'added';
  }

  onNameFilter(event: AutoCompleteCompleteEvent): void {
    this.state.onFilterName(event);
  }

  onBarcodeFilter(event: AutoCompleteCompleteEvent): void {
    this.state.onFilterBarcode(event);
  }

  onSupplierFilter(event: AutoCompleteCompleteEvent): void {
    this.state.onFilterSupplier(event);
  }

  requestScanner(): void {
    this.scannerRequested.emit();
    this.scanRequested.emit();
  }
}
