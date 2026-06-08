import { Component, EventEmitter, inject, Output, signal } from '@angular/core';
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
import { InventoryBarcodeFieldComponent } from '../barcode-field.component';
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
    InventoryBarcodeFieldComponent,
    BatchHsnPickerDialogComponent,
  ],
  templateUrl: './batch-row-form.component.html',
})
export class BatchRowFormComponent {
  readonly state = inject(BatchRowFormStateService);
  readonly form = this.state.form;

  readonly optionalDetailsExpanded = signal(false);
  readonly pricingGuardVisible = signal(false);
  readonly barcodeReplaceConfirmVisible = signal(false);
  private pendingGeneratedBarcode: string | null = null;

  @Output() readonly rowSubmitted = new EventEmitter<InventoryInboundDraftRow>();
  @Output() readonly rowAdded = new EventEmitter<InventoryInboundDraftRow>();
  @Output() readonly scannerRequested = new EventEmitter<void>();
  @Output() readonly scanRequested = new EventEmitter<void>();

  toggleOptionalDetails(): void {
    this.optionalDetailsExpanded.update((value) => !value);
  }

  expandOptionalDetails(): void {
    this.optionalDetailsExpanded.set(true);
  }

  collapseOptionalDetails(): void {
    this.optionalDetailsExpanded.set(false);
  }

  hasMissingRequiredPricing(): boolean {
    const mrp = Number(this.form.controls.mrp.value);
    const salesPrice = Number(this.form.controls.salesPrice.value);
    return mrp <= 0 || salesPrice <= 0;
  }

  showPricingGuard(): void {
    this.pricingGuardVisible.set(true);
  }

  clearPricingGuard(): void {
    this.pricingGuardVisible.set(false);
  }

  showPricingReviewRequired(): void {
    this.expandOptionalDetails();
    this.form.controls.mrp.markAsTouched();
    this.form.controls.salesPrice.markAsTouched();
    this.showPricingGuard();
  }

  submit(): void {
    this.tryAddRow();
  }

  tryAddRow(): boolean {
    if (this.hasMissingRequiredPricing()) {
      this.showPricingReviewRequired();
      return false;
    }

    const row = this.state.buildDraftRow();
    if (!row) {
      this.state.form.markAllAsTouched();
      return false;
    }

    this.rowSubmitted.emit(row);
    this.rowAdded.emit(row);
    this.state.resetForm();
    this.clearPricingGuard();
    this.collapseOptionalDetails();
    return true;
  }

  populateFromRow(row: InventoryInboundDraftRow): void {
    this.state.loadDraftRow(row);
    this.expandOptionalDetails();
    this.clearPricingGuard();
  }

  resetForm(): void {
    this.state.resetForm();
    this.clearPricingGuard();
    this.collapseOptionalDetails();
  }

  async handleBarcode(barcode: string): Promise<'added' | 'review'> {
    const scanResult = await this.state.prepareScannedRow(barcode);
    if (scanResult.status !== 'added') {
      if (scanResult.status === 'missingPricing') {
        this.showPricingReviewRequired();
      }
      return 'review';
    }

    this.rowSubmitted.emit(scanResult.row);
    this.rowAdded.emit(scanResult.row);
    this.state.resetForm();
    this.clearPricingGuard();
    this.collapseOptionalDetails();
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

  async onGenerateBarcode(): Promise<void> {
    const result = await this.state.generateBarcode();
    if ('error' in result) {
      return;
    }
    if (result.needsConfirm) {
      this.pendingGeneratedBarcode = result.barcode;
      this.barcodeReplaceConfirmVisible.set(true);
    }
  }

  confirmBarcodeReplace(): void {
    if (this.pendingGeneratedBarcode) {
      this.state.patchGeneratedBarcode(this.pendingGeneratedBarcode);
    }
    this.pendingGeneratedBarcode = null;
    this.barcodeReplaceConfirmVisible.set(false);
  }

  cancelBarcodeReplace(): void {
    this.pendingGeneratedBarcode = null;
    this.barcodeReplaceConfirmVisible.set(false);
  }
}
