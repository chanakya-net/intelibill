import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { AvatarModule } from 'primeng/avatar';
import { DialogModule } from 'primeng/dialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { TagModule } from 'primeng/tag';
import { AutoCompleteModule, AutoCompleteCompleteEvent } from 'primeng/autocomplete';
import { SelectModule } from 'primeng/select';

import { finalize } from 'rxjs';

import {
  AdjustInventoryBatchRequest,
  InventoryBatchDto,
  InventoryAdjustmentDirection,
  InventoryAdjustmentReason,
  UpdateInventoryBatchRequest,
} from '../../services/inventory.models';
import { InventoryService } from '../../services/inventory.service';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import { Supplier } from '../../../suppliers/services/supplier.service';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
  CURRENCY_SELECT_PT,
} from '../../../../shared/primeng-pt.config';
import { TableFilterBarComponent } from '../../../../shared/components/table-filter-bar/table-filter-bar.component';
import { formatLocalIsoDate, formatUtcIsoInstant } from '../../../../shared/utils/date-time.util';

interface SelectOption<T extends string | boolean> {
  label: string;
  value: T;
}

interface AdjustmentPreview {
  readonly batchQuantityBefore: number;
  readonly batchQuantityAfter: number;
  readonly unitCost: number;
  readonly costImpact: number;
}

@Component({
  selector: 'app-inventory-batches-list-page',
  standalone: true,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslocoPipe,
    DecimalPipe,
    ButtonModule,
    CardModule,
    AvatarModule,
    DialogModule,
    IconFieldModule,
    InputIconModule,
    InputGroupAddonModule,
    InputGroupModule,
    InputNumberModule,
    InputTextModule,
    TableModule,
    TableFilterBarComponent,
    TextareaModule,
    ToastModule,
    TagModule,
    AutoCompleteModule,
    SelectModule,
  ],
  providers: [MessageService],
  templateUrl: './inventory-batches-list-page.component.html',
  styleUrl: './inventory-batches-list-page.component.scss',
})
export class InventoryBatchesListPageComponent {
  private readonly inventoryService = inject(InventoryService);
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly formBuilder = inject(FormBuilder);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;
  readonly currencySelectPt = CURRENCY_SELECT_PT;

  readonly batches = signal<InventoryBatchDto[]>([]);
  readonly tableBatches = computed(() => [...this.batches()]);
  readonly filteredBatches = computed(() => {
    const q = this.searchValue().toLowerCase();
    if (!q) return [...this.batches()];
    return this.batches().filter(
      (b) =>
        b.itemName.toLowerCase().includes(q) ||
        b.barcode.toLowerCase().includes(q) ||
        b.batchNumber.toLowerCase().includes(q),
    );
  });
  readonly loading = signal(false);
  readonly isEditDialogOpen = signal(false);
  readonly isAdjustmentDialogOpen = signal(false);
  readonly isSaving = signal(false);
  readonly isAdjustmentSaving = signal(false);
  readonly selectedBatch = signal<InventoryBatchDto | null>(null);
  readonly supplierSuggestions = signal<string[]>([]);
  readonly searchValue = signal('');
  readonly suppliers = this.suppliersFacade.suppliers;
  readonly taxModeOptions = signal([
    { label: 'With Tax', value: true },
    { label: 'Without Tax', value: false },
  ]);
  readonly directionOptions = signal<SelectOption<InventoryAdjustmentDirection>[]>([
    { label: this.translate('inventory.adjustmentDirection.decrease'), value: 'Decrease' },
    { label: this.translate('inventory.adjustmentDirection.increase'), value: 'Increase' },
  ]);
  private readonly decreaseReasonOptions: SelectOption<InventoryAdjustmentReason>[] = [
    { label: this.translate('inventory.adjustmentReason.damaged'), value: 'Damaged' },
    { label: this.translate('inventory.adjustmentReason.expired'), value: 'Expired' },
    { label: this.translate('inventory.adjustmentReason.stolen'), value: 'Stolen' },
    { label: this.translate('inventory.adjustmentReason.missingLost'), value: 'MissingLost' },
    {
      label: this.translate('inventory.adjustmentReason.stockCountCorrection'),
      value: 'StockCountCorrection',
    },
    { label: this.translate('inventory.adjustmentReason.otherLoss'), value: 'OtherLoss' },
  ];
  private readonly increaseReasonOptions: SelectOption<InventoryAdjustmentReason>[] = [
    { label: this.translate('inventory.adjustmentReason.foundStock'), value: 'FoundStock' },
    {
      label: this.translate('inventory.adjustmentReason.stockCountCorrection'),
      value: 'StockCountCorrection',
    },
    {
      label: this.translate('inventory.adjustmentReason.returnRestockCorrection'),
      value: 'ReturnRestockCorrection',
    },
    { label: this.translate('inventory.adjustmentReason.otherGain'), value: 'OtherGain' },
  ];

  private readonly adjustmentFormValue = signal({
    direction: 'Decrease' as InventoryAdjustmentDirection,
    reason: 'Damaged' as InventoryAdjustmentReason,
    quantity: 1,
    performedAt: '',
    notes: '',
  });

  readonly adjustmentReasonOptions = computed(() =>
    this.adjustmentFormValue().direction === 'Increase'
      ? this.increaseReasonOptions
      : this.decreaseReasonOptions,
  );

  readonly adjustmentPreview = computed<AdjustmentPreview | null>(() => {
    const batch = this.selectedBatch();
    if (!batch) return null;

    const formValue = this.adjustmentFormValue();
    const quantity = Number(formValue.quantity) || 0;
    const signedQuantity = formValue.direction === 'Increase' ? quantity : -quantity;
    const batchQuantityAfter = Number((batch.quantity + signedQuantity).toFixed(2));
    const costImpact = Number((signedQuantity * batch.costPrice).toFixed(2));

    return {
      batchQuantityBefore: batch.quantity,
      batchQuantityAfter,
      unitCost: batch.costPrice,
      costImpact,
    };
  });

  readonly editForm = this.formBuilder.nonNullable.group({
    newBatchNumber: ['', [Validators.maxLength(80)]],
    quantity: [0, [Validators.required, Validators.min(0)]],
    costPrice: [0, [Validators.required, Validators.min(0)]],
    mrp: [0, [Validators.required, Validators.min(0)]],
    salesPrice: [0, [Validators.required, Validators.min(0)]],
    taxRatePercent: [0, [Validators.required, Validators.min(0)]],
    taxIncluded: [false, [Validators.required]],
    expiryDate: [''],
    manufacturingDate: [''],
    supplierName: [''],
    notes: [''],
    entryDate: [''],
  });

  readonly adjustmentForm = this.formBuilder.nonNullable.group({
    direction: ['Decrease' as InventoryAdjustmentDirection, [Validators.required]],
    reason: ['Damaged' as InventoryAdjustmentReason, [Validators.required]],
    quantity: [1, [Validators.required, Validators.min(0.01), this.maxFractionDigits(2)]],
    performedAt: [''],
    notes: [''],
  });

  constructor() {
    this.adjustmentForm.valueChanges.subscribe(() => {
      this.adjustmentFormValue.set(this.adjustmentForm.getRawValue());
    });

    this.adjustmentForm.controls.direction.valueChanges.subscribe((direction) => {
      const availableReasons =
        direction === 'Increase' ? this.increaseReasonOptions : this.decreaseReasonOptions;
      const currentReason = this.adjustmentForm.controls.reason.value;
      if (!availableReasons.some((option) => option.value === currentReason)) {
        this.adjustmentForm.controls.reason.setValue(availableReasons[0].value);
      }

      this.updateAdjustmentQuantityValidators();
    });

    this.adjustmentForm.controls.reason.valueChanges.subscribe(() => {
      this.updateAdjustmentNotesValidators();
    });

    this.suppliersFacade.load();
    this.loadBatches();
  }

  loadBatches(): void {
    this.loading.set(true);
    this.inventoryService
      .getInventoryBatches()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (data) => this.batches.set([...data]),
        error: () => this.showError('inventory.loadBatchesError'),
      });
  }

  onEditBatch(batch: InventoryBatchDto): void {
    this.selectedBatch.set(batch);
    this.editForm.patchValue({
      newBatchNumber: '', // Start empty for user to provide or system to generate
      quantity: batch.quantity,
      costPrice: batch.costPrice,
      mrp: batch.mrp,
      salesPrice: batch.salesPrice,
      taxRatePercent: batch.taxRatePercent,
      taxIncluded: batch.taxIncluded,
      expiryDate: batch.expiryDate ?? '',
      manufacturingDate: batch.manufacturingDate ?? '',
      supplierName: batch.supplierName ?? this.getSupplierDisplayName(batch.supplierId),
      notes: '',
      entryDate: formatLocalIsoDate(new Date()),
    });
    this.isEditDialogOpen.set(true);
  }

  onSaveEdit(): void {
    const batch = this.selectedBatch();
    if (!batch || this.editForm.invalid) return;

    this.isSaving.set(true);
    const formValue = this.editForm.getRawValue();
    const payload: UpdateInventoryBatchRequest = {
      newBatchNumber: this.nullable(formValue.newBatchNumber),
      quantity: formValue.quantity,
      costPrice: formValue.costPrice,
      mrp: formValue.mrp,
      salesPrice: formValue.salesPrice,
      taxRatePercent: formValue.taxRatePercent,
      taxIncluded: formValue.taxIncluded,
      expiryDate: this.nullable(formValue.expiryDate),
      manufacturingDate: this.nullable(formValue.manufacturingDate),
      supplierId: this.resolveSupplierId(formValue.supplierName),
      notes: this.nullable(formValue.notes) ?? 'Batch Correction',
      entryDate: this.nullable(formValue.entryDate),
    };

    this.inventoryService
      .updateInventoryBatch(batch.id, payload)
      .pipe(finalize(() => this.isSaving.set(false)))
      .subscribe({
        next: () => {
          this.showSuccess('inventory.batchCorrected');
          this.isEditDialogOpen.set(false);
          this.loadBatches();
        },
        error: (err) => {
          const detail = err.error?.detail || this.translate('inventory.updateBatchError');
          this.messageService.add({ severity: 'error', summary: 'Error', detail });
        },
      });
  }

  onAdjustBatch(batch: InventoryBatchDto): void {
    if (batch.isVoided) return;

    this.selectedBatch.set(batch);
    this.adjustmentForm.reset({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 1,
      performedAt: '',
      notes: '',
    });
    this.updateAdjustmentQuantityValidators();
    this.updateAdjustmentNotesValidators();
    this.adjustmentFormValue.set(this.adjustmentForm.getRawValue());
    this.isAdjustmentDialogOpen.set(true);
  }

  onSaveAdjustment(): void {
    const batch = this.selectedBatch();
    if (!batch || this.adjustmentForm.invalid || this.isAdjustmentSaving()) return;

    this.isAdjustmentSaving.set(true);
    const formValue = this.adjustmentForm.getRawValue();
    const payload: AdjustInventoryBatchRequest = {
      direction: formValue.direction,
      reason: formValue.reason,
      quantity: formValue.quantity,
      performedAt: this.toIsoTimestamp(formValue.performedAt),
      notes: this.nullable(formValue.notes),
    };

    this.inventoryService
      .adjustInventoryBatch(batch.id, payload)
      .pipe(finalize(() => this.isAdjustmentSaving.set(false)))
      .subscribe({
        next: () => {
          this.showSuccess('inventory.batchAdjusted');
          this.isAdjustmentDialogOpen.set(false);
          this.loadBatches();
        },
        error: (err) => {
          const detail = err.error?.detail || this.translate('inventory.adjustBatchError');
          this.messageService.add({ severity: 'error', summary: 'Error', detail });
        },
      });
  }

  onVoidBatch(batchId: string): void {
    this.inventoryService.voidInventoryBatch(batchId).subscribe({
      next: () => {
        this.showSuccess('inventory.batchVoided');
        this.loadBatches();
      },
      error: () => this.showError('inventory.voidBatchError'),
    });
  }

  onFilterSupplier(event: AutoCompleteCompleteEvent): void {
    const normalized = event.query.trim().toLowerCase();
    const matches = this.suppliers()
      .filter((supplier) => supplier.name.toLowerCase().includes(normalized))
      .slice(0, 15)
      .map((supplier) => supplier.name);

    this.supplierSuggestions.set(matches);
  }

  getSupplierDisplayName(supplierId: string | null): string {
    if (!supplierId) return '-';
    return (
      this.suppliers().find((supplier) => supplier.supplierId === supplierId)?.name ?? supplierId
    );
  }

  private findSupplierByName(name: string): Supplier | undefined {
    const normalized = name.trim().toLowerCase();
    return this.suppliers().find((supplier) => supplier.name.toLowerCase() === normalized);
  }

  private resolveSupplierId(value: string): string | null {
    const normalized = value.trim();
    if (!normalized) return null;
    return this.findSupplierByName(normalized)?.supplierId ?? null;
  }

  private nullable(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  private toIsoTimestamp(value: string): string | null {
    const normalized = value.trim();
    if (!normalized) return null;

    const date = new Date(normalized);
    return Number.isNaN(date.getTime()) ? normalized : formatUtcIsoInstant(date);
  }

  private updateAdjustmentQuantityValidators(): void {
    const validators: ValidatorFn[] = [
      Validators.required,
      Validators.min(0.01),
      this.maxFractionDigits(2),
    ];

    const batch = this.selectedBatch();
    if (batch && this.adjustmentForm.controls.direction.value === 'Decrease') {
      validators.push(Validators.max(batch.quantity));
    }

    this.adjustmentForm.controls.quantity.setValidators(validators);
    this.adjustmentForm.controls.quantity.updateValueAndValidity({ emitEvent: false });
  }

  private updateAdjustmentNotesValidators(): void {
    const reason = this.adjustmentForm.controls.reason.value;
    const validators =
      reason === 'OtherLoss' || reason === 'OtherGain'
        ? [Validators.required, Validators.maxLength(500)]
        : [Validators.maxLength(500)];

    this.adjustmentForm.controls.notes.setValidators(validators);
    this.adjustmentForm.controls.notes.updateValueAndValidity({ emitEvent: false });
  }

  private maxFractionDigits(digits: number): ValidatorFn {
    return (control: AbstractControl<number>): ValidationErrors | null => {
      const value = control.value;
      if (value === null || value === undefined) return null;

      const decimalPart = value.toString().split('.')[1];
      return decimalPart && decimalPart.length > digits
        ? { maxFractionDigits: { requiredDigits: digits } }
        : null;
    };
  }

  private showSuccess(messageKey: string): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate(messageKey),
      life: 3000,
    });
  }

  private showError(messageKey: string): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate(messageKey),
      life: 3500,
    });
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }

  productInitials(name: string): string {
    const words = name.trim().split(/\s+/);
    if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  productAvatarColor(name: string): string {
    const colors = [
      '#b45309',
      '#0369a1',
      '#15803d',
      '#7c3aed',
      '#be185d',
      '#c2410c',
      '#0f766e',
      '#1d4ed8',
    ];
    let hash = 0;
    for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return colors[Math.abs(hash) % colors.length];
  }
}
