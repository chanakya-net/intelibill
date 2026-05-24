import { Component, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { AutoCompleteModule, AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { SelectModule } from 'primeng/select';
import { finalize } from 'rxjs';
import { AdjustInventoryBatchRequest, BatchFilters, InventoryAdjustmentDirection, InventoryAdjustmentReason, InventoryBatchDto, UpdateInventoryBatchRequest } from '../../services/inventory.models';
import { InventoryService } from '../../services/inventory.service';
import { BatchesFilterBarComponent } from '../../components/batches-list/batches-filter-bar.component';
import { BatchesTableComponent, BatchTableAction } from '../../components/batches-list/batches-table.component';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import { Supplier } from '../../../suppliers/services/supplier.service';
import { CURRENCY_ADDON_PT, CURRENCY_INPUT_GROUP_PT, CURRENCY_INPUT_NUMBER_PT, CURRENCY_SELECT_PT } from '../../../../shared/primeng-pt.config';
import { formatLocalIsoDate, formatUtcIsoInstant } from '../../../../shared/utils/date-time.util';
interface SelectOption<T extends string | boolean> {
  label: string;
  value: T;
}
@Component({
  selector: 'app-inventory-batches-list-page',
  standalone: true,
  imports: [ReactiveFormsModule, TranslocoPipe, DecimalPipe, ButtonModule, DialogModule, InputGroupAddonModule, InputGroupModule, InputNumberModule, InputTextModule, TextareaModule, ToastModule, AutoCompleteModule, SelectModule, BatchesFilterBarComponent, BatchesTableComponent],
  providers: [MessageService],
  templateUrl: './inventory-batches-list-page.component.html',
  styleUrl: './inventory-batches-list-page.component.scss',
})
export class InventoryBatchesListPageComponent {
  private readonly inventoryService = inject(InventoryService);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly translocoService = inject(TranslocoService);
  private readonly messageService = inject(MessageService);
  private readonly formBuilder = inject(FormBuilder);
  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;
  readonly currencySelectPt = CURRENCY_SELECT_PT;
  readonly batches = signal<InventoryBatchDto[]>([]);
  readonly loading = signal(false);
  readonly isEditDialogOpen = signal(false);
  readonly isAdjustmentDialogOpen = signal(false);
  readonly isSaving = signal(false);
  readonly isAdjustmentSaving = signal(false);
  readonly selectedBatch = signal<InventoryBatchDto | null>(null);
  readonly supplierSuggestions = signal<string[]>([]);
  readonly suppliers = this.suppliersFacade.suppliers;
  readonly batchFilters = signal<BatchFilters>({ search: '', status: 'all', fromDate: '', toDate: '' });
  readonly filteredBatches = computed(() => {
    const filters = this.batchFilters();
    const query = filters.search.trim().toLowerCase();
    const fromBoundaryMs = this.toDateBoundary(filters.fromDate, false);
    const toBoundaryMs = this.toDateBoundary(filters.toDate, true);
    return this.batches().filter((batch) => {
      if (!this.matchesStatusFilter(batch, filters.status)) return false;
      if (query && !this.matchesSearchFilter(batch, query)) return false;
      if (!this.matchesDateRange(batch, fromBoundaryMs, toBoundaryMs)) return false;
      return true;
    });
  });
  readonly taxModeOptions = signal([{ label: 'With Tax', value: true }, { label: 'Without Tax', value: false }]);
  readonly directionOptions = signal<SelectOption<InventoryAdjustmentDirection>[]>([{ label: this.translate('inventory.adjustmentDirection.decrease'), value: 'Decrease' }, { label: this.translate('inventory.adjustmentDirection.increase'), value: 'Increase' }]);
  private readonly decreaseReasonOptions: SelectOption<InventoryAdjustmentReason>[] = [
    { label: this.translate('inventory.adjustmentReason.damaged'), value: 'Damaged' },
    { label: this.translate('inventory.adjustmentReason.expired'), value: 'Expired' },
    { label: this.translate('inventory.adjustmentReason.stolen'), value: 'Stolen' },
    { label: this.translate('inventory.adjustmentReason.missingLost'), value: 'MissingLost' },
    { label: this.translate('inventory.adjustmentReason.stockCountCorrection'), value: 'StockCountCorrection' },
    { label: this.translate('inventory.adjustmentReason.otherLoss'), value: 'OtherLoss' },
  ];
  private readonly increaseReasonOptions: SelectOption<InventoryAdjustmentReason>[] = [
    { label: this.translate('inventory.adjustmentReason.foundStock'), value: 'FoundStock' },
    { label: this.translate('inventory.adjustmentReason.stockCountCorrection'), value: 'StockCountCorrection' },
    { label: this.translate('inventory.adjustmentReason.returnRestockCorrection'), value: 'ReturnRestockCorrection' },
    { label: this.translate('inventory.adjustmentReason.otherGain'), value: 'OtherGain' },
  ];
  private readonly adjustmentFormValue = signal({
    direction: 'Decrease' as InventoryAdjustmentDirection,
    reason: 'Damaged' as InventoryAdjustmentReason,
    quantity: 1,
    performedAt: '',
    notes: '',
  });
  readonly adjustmentReasonOptions = computed(() => this.adjustmentFormValue().direction === 'Increase' ? this.increaseReasonOptions : this.decreaseReasonOptions);
  readonly adjustmentPreview = computed(() => {
    const batch = this.selectedBatch(); if (!batch) return null;
    const value = this.adjustmentFormValue();
    const quantity = Number(value.quantity) || 0;
    const signed = value.direction === 'Increase' ? quantity : -quantity;
    return {
      batchQuantityBefore: batch.quantity,
      batchQuantityAfter: Number((batch.quantity + signed).toFixed(2)),
      unitCost: batch.costPrice,
      costImpact: Number((signed * batch.costPrice).toFixed(2)),
    };
  });
  readonly editForm = this.formBuilder.nonNullable.group({ newBatchNumber: ['', [Validators.maxLength(80)]], quantity: [0, [Validators.required, Validators.min(0)]], costPrice: [0, [Validators.required, Validators.min(0)]], mrp: [0, [Validators.required, Validators.min(0)]], salesPrice: [0, [Validators.required, Validators.min(0)]], taxRatePercent: [0, [Validators.required, Validators.min(0)]], taxIncluded: [false, [Validators.required]], expiryDate: [''], manufacturingDate: [''], supplierName: [''], notes: [''], entryDate: [''], });
  readonly adjustmentForm = this.formBuilder.nonNullable.group({ direction: ['Decrease' as InventoryAdjustmentDirection, [Validators.required]], reason: ['Damaged' as InventoryAdjustmentReason, [Validators.required]], quantity: [1, [Validators.required, Validators.min(0.01), this.maxFractionDigits(2)]], performedAt: [''], notes: [''], });
  constructor() {
    this.adjustmentForm.valueChanges.subscribe(() => this.adjustmentFormValue.set(this.adjustmentForm.getRawValue()));
    this.adjustmentForm.controls.direction.valueChanges.subscribe((direction) => {
      const options = direction === 'Increase' ? this.increaseReasonOptions : this.decreaseReasonOptions;
      const currentReason = this.adjustmentForm.controls.reason.value;
      if (!options.some((option) => option.value === currentReason)) {
        this.adjustmentForm.controls.reason.setValue(options[0].value);
      }
      this.updateAdjustmentQuantityValidators();
    });
    this.adjustmentForm.controls.reason.valueChanges.subscribe(() => this.updateAdjustmentNotesValidators());
    this.suppliersFacade.load();
    this.loadBatches();
  }
  onFiltersChange(filters: BatchFilters): void { this.batchFilters.set(filters); }
  onBatchTableAction(event: BatchTableAction): void {
    const batch = this.batches().find((entry) => entry.id === event.batchId);
    if (!batch) return;
    if (event.action === 'edit') return this.onEditBatch(batch);
    if (event.action === 'adjust') return this.onAdjustBatch(batch);
    if (event.action === 'void') return this.onVoidBatch(batch.id);
    this.onEditBatch(batch);
  }

  onBatchTableSelect(batchId: string): void {
    const batch = this.batches().find((entry) => entry.id === batchId);
    if (!batch) return;
    this.onEditBatch(batch);
  }
  loadBatches(): void {
    this.loading.set(true);
    this.inventoryService.getInventoryBatches().pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (data) => this.batches.set([...data]),
      error: () => this.showError('inventory.loadBatchesError'),
    });
  }
  onEditBatch(batch: InventoryBatchDto): void {
    this.selectedBatch.set(batch);
    this.editForm.patchValue({
      newBatchNumber: '',
      quantity: batch.quantity,
      costPrice: batch.costPrice,
      mrp: batch.mrp,
      salesPrice: batch.salesPrice,
      taxRatePercent: batch.taxRatePercent,
      taxIncluded: batch.taxIncluded,
      expiryDate: batch.expiryDate ?? '',
      manufacturingDate: batch.manufacturingDate ?? '',
      supplierName: batch.supplierId ? this.getSupplierDisplayName(batch.supplierId) : '',
      notes: '',
      entryDate: formatLocalIsoDate(new Date()),
    });
    this.isEditDialogOpen.set(true);
  }
  onSaveEdit(): void {
    const batch = this.selectedBatch();
    if (!batch || this.editForm.invalid) return;
    this.isSaving.set(true);
    const value = this.editForm.getRawValue();
    const payload: UpdateInventoryBatchRequest = {
      newBatchNumber: this.nullable(value.newBatchNumber),
      quantity: value.quantity,
      costPrice: value.costPrice,
      mrp: value.mrp,
      salesPrice: value.salesPrice,
      taxRatePercent: value.taxRatePercent,
      taxIncluded: value.taxIncluded,
      expiryDate: this.nullable(value.expiryDate),
      manufacturingDate: this.nullable(value.manufacturingDate),
      supplierId: this.resolveSupplierId(value.supplierName),
      notes: this.nullable(value.notes) ?? 'Batch Correction',
      entryDate: this.nullable(value.entryDate),
    };
    this.inventoryService.updateInventoryBatch(batch.id, payload).pipe(finalize(() => this.isSaving.set(false))).subscribe({
      next: () => { this.showSuccess('inventory.batchCorrected'); this.isEditDialogOpen.set(false); this.loadBatches(); },
      error: (err) => {
        const detail = err.error?.detail || this.translate('inventory.updateBatchError');
        this.messageService.add({ severity: 'error', summary: 'Error', detail });
      },
    });
  }
  onAdjustBatch(batch: InventoryBatchDto): void {
    if (batch.isVoided) return;
    this.selectedBatch.set(batch);
    this.adjustmentForm.reset({ direction: 'Decrease', reason: 'Damaged', quantity: 1, performedAt: '', notes: '' });
    this.updateAdjustmentQuantityValidators();
    this.updateAdjustmentNotesValidators();
    this.adjustmentFormValue.set(this.adjustmentForm.getRawValue());
    this.isAdjustmentDialogOpen.set(true);
  }
  onSaveAdjustment(): void {
    const batch = this.selectedBatch();
    if (!batch || this.adjustmentForm.invalid || this.isAdjustmentSaving()) return;
    this.isAdjustmentSaving.set(true);
    const value = this.adjustmentForm.getRawValue();
    const payload: AdjustInventoryBatchRequest = {
      direction: value.direction,
      reason: value.reason,
      quantity: value.quantity,
      performedAt: this.toIsoTimestamp(value.performedAt),
      notes: this.nullable(value.notes),
    };
    this.inventoryService.adjustInventoryBatch(batch.id, payload).pipe(finalize(() => this.isAdjustmentSaving.set(false))).subscribe({
      next: () => { this.showSuccess('inventory.batchAdjusted'); this.isAdjustmentDialogOpen.set(false); this.loadBatches(); },
      error: (err) => {
        const detail = err.error?.detail || this.translate('inventory.adjustBatchError');
        this.messageService.add({ severity: 'error', summary: 'Error', detail });
      },
    });
  }
  onVoidBatch(batchId: string): void {
    this.inventoryService.voidInventoryBatch(batchId).subscribe({
      next: () => { this.showSuccess('inventory.batchVoided'); this.loadBatches(); },
      error: () => this.showError('inventory.voidBatchError'),
    });
  }
  onFilterSupplier(event: AutoCompleteCompleteEvent): void {
    const normalized = event.query.trim().toLowerCase();
    this.supplierSuggestions.set(this.suppliers().filter((supplier) => supplier.name.toLowerCase().includes(normalized)).slice(0, 15).map((supplier) => supplier.name));
  }
  getSupplierDisplayName(supplierId: string | null): string {
    if (!supplierId) return '-';
    return this.suppliers().find((supplier) => supplier.supplierId === supplierId)?.name ?? supplierId;
  }
  private findSupplierByName(name: string): Supplier | undefined {
    return this.suppliers().find((supplier) => supplier.name.toLowerCase() === name.trim().toLowerCase());
  }
  private resolveSupplierId(value: string): string | null { return this.findSupplierByName(value.trim())?.supplierId ?? null; }
  private nullable(value: string): string | null { return value.trim().length > 0 ? value.trim() : null; }
  private toIsoTimestamp(value: string): string | null {
    const normalized = value.trim(); if (!normalized) return null;
    const date = new Date(normalized);
    return Number.isNaN(date.getTime()) ? normalized : formatUtcIsoInstant(date);
  }
  private updateAdjustmentQuantityValidators(): void {
    const validators: ValidatorFn[] = [Validators.required, Validators.min(0.01), this.maxFractionDigits(2)];
    const batch = this.selectedBatch();
    if (batch && this.adjustmentForm.controls.direction.value === 'Decrease') validators.push(Validators.max(batch.quantity));
    this.adjustmentForm.controls.quantity.setValidators(validators);
    this.adjustmentForm.controls.quantity.updateValueAndValidity({ emitEvent: false });
  }
  private updateAdjustmentNotesValidators(): void {
    const reason = this.adjustmentForm.controls.reason.value;
    const validators = reason === 'OtherLoss' || reason === 'OtherGain' ? [Validators.required, Validators.maxLength(500)] : [Validators.maxLength(500)];
    this.adjustmentForm.controls.notes.setValidators(validators);
    this.adjustmentForm.controls.notes.updateValueAndValidity({ emitEvent: false });
  }
  private maxFractionDigits(digits: number): ValidatorFn {
    return (control: AbstractControl<number>): ValidationErrors | null => {
      const decimalPart = control.value?.toString().split('.')[1];
      return decimalPart && decimalPart.length > digits ? { maxFractionDigits: { requiredDigits: digits } } : null;
    };
  }
  private matchesSearchFilter(batch: InventoryBatchDto, query: string): boolean {
    return batch.itemName.toLowerCase().includes(query) || batch.barcode.toLowerCase().includes(query) || batch.batchNumber.toLowerCase().includes(query);
  }
  private matchesStatusFilter(batch: InventoryBatchDto, status: BatchFilters['status']): boolean { return status === 'all' ? true : status === 'active' ? !batch.isVoided : batch.isVoided; }
  private matchesDateRange(batch: InventoryBatchDto, fromBoundaryMs: number | null, toBoundaryMs: number | null): boolean {
    if (!fromBoundaryMs && !toBoundaryMs) return true;
    const batchCreatedAt = Date.parse(batch.createdAt);
    if (Number.isNaN(batchCreatedAt)) return true;
    if (fromBoundaryMs && batchCreatedAt < fromBoundaryMs) return false;
    if (toBoundaryMs && batchCreatedAt > toBoundaryMs) return false;
    return true;
  }
  private toDateBoundary(value: string, includeDayEnd: boolean): number | null {
    const normalized = value.trim();
    if (!normalized) return null;
    const date = new Date(normalized);
    if (Number.isNaN(date.getTime())) return null;
    if (includeDayEnd) date.setHours(23, 59, 59, 999);
    else date.setHours(0, 0, 0, 0);
    return date.getTime();
  }
  private showSuccess(messageKey: string): void {
    this.messageService.add({ severity: 'success', summary: this.translate(messageKey), life: 3000 });
  }
  private showError(messageKey: string): void {
    this.messageService.add({ severity: 'error', summary: this.translate(messageKey), life: 3500 });
  }
  private translate(key: string): string { return this.translocoService.translate(key); }
}
