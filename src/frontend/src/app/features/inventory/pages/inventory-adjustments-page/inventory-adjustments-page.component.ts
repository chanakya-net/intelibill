import { DecimalPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';

import { finalize } from 'rxjs';

import {
  AdjustInventoryBatchRequest,
  InventoryAdjustmentDirection,
  InventoryAdjustmentHistoryItem,
  InventoryAdjustmentHistoryQuery,
  InventoryAdjustmentReason,
  InventoryBatchDto,
  InventoryService,
} from '../../services/inventory.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { formatUtcIsoInstant } from '../../../../shared/utils/date-time.util';

interface SelectOption<T extends string | boolean> {
  readonly label: string;
  readonly value: T;
}

@Component({
  selector: 'app-inventory-adjustments-page',
  standalone: true,
  imports: [
    DatePipe,
    DecimalPipe,
    FormsModule,
    ReactiveFormsModule,
    TranslocoPipe,
    AutoCompleteModule,
    ButtonModule,
    CardModule,
    CheckboxModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    ToastModule,
  ],
  providers: [MessageService],
  templateUrl: './inventory-adjustments-page.component.html',
  styleUrl: './inventory-adjustments-page.component.scss',
})
export class InventoryAdjustmentsPageComponent {
  private readonly inventoryService = inject(InventoryService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly messageService = inject(MessageService);
  private readonly translocoService = inject(TranslocoService);
  private readonly authService = inject(AuthService);

  readonly adjustments = signal<InventoryAdjustmentHistoryItem[]>([]);
  readonly batches = signal<InventoryBatchDto[]>([]);
  readonly batchSuggestions = signal<InventoryBatchDto[]>([]);
  readonly selectedBatch = signal<InventoryBatchDto | null>(null);
  readonly selectedAdjustment = signal<InventoryAdjustmentHistoryItem | null>(null);
  readonly loading = signal(false);
  readonly loadingBatches = signal(false);
  readonly saving = signal(false);
  readonly voidSaving = signal(false);
  readonly isAdjustmentDialogOpen = signal(false);
  readonly isVoidDialogOpen = signal(false);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) return '';
    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canCreateAdjustments = computed(() => {
    const role = this.activeShopRole().toLowerCase();
    return role === 'owner' || role === 'manager';
  });
  readonly canVoidAdjustments = computed(() => this.activeShopRole().toLowerCase() === 'owner');
  private readonly adjustmentDirectionValue = signal<InventoryAdjustmentDirection>('Decrease');

  readonly availableBatches = computed(() => this.batches().filter((batch) => !batch.isVoided));
  readonly totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize()));
  readonly selectedBatchDecreaseBlocked = computed(() => {
    const batch = this.selectedBatch();
    return (
      !!batch &&
      batch.quantity <= 0 &&
      this.adjustmentDirectionValue() === 'Decrease'
    );
  });
  readonly selectedBatchLabel = computed(() => {
    const batch = this.selectedBatch();
    return batch
      ? `${batch.itemName} · ${batch.batchNumber} · ${this.translate('inventory.quantity')}: ${batch.quantity}`
      : '';
  });

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

  readonly reasonOptions = computed<SelectOption<InventoryAdjustmentReason>[]>(() => {
    const direction =
      this.filterForm.controls.direction.value ?? this.adjustmentForm.controls.direction.value;
    return direction === 'Increase' ? this.increaseReasonOptions : this.decreaseReasonOptions;
  });
  readonly adjustmentReasonOptions = computed<SelectOption<InventoryAdjustmentReason>[]>(() =>
    this.adjustmentForm.controls.direction.value === 'Increase'
      ? this.increaseReasonOptions
      : this.decreaseReasonOptions,
  );
  readonly itemOptions = computed(() => {
    const seen = new Map<string, SelectOption<string>>();
    for (const batch of this.availableBatches()) {
      seen.set(batch.itemId, { label: batch.itemName, value: batch.itemId });
    }
    return [...seen.values()].sort((a, b) => a.label.localeCompare(b.label));
  });
  readonly batchOptions = computed(() =>
    this.availableBatches()
      .map((batch) => ({ label: `${batch.batchNumber} · ${batch.itemName}`, value: batch.id }))
      .sort((a, b) => a.label.localeCompare(b.label)),
  );

  readonly filterForm = this.formBuilder.group({
    itemId: [''],
    batchId: [''],
    direction: [null as InventoryAdjustmentDirection | null],
    reason: [null as InventoryAdjustmentReason | null],
    from: [''],
    to: [''],
    includeVoided: [false],
  });

  readonly adjustmentForm = this.formBuilder.nonNullable.group({
    direction: ['Decrease' as InventoryAdjustmentDirection, [Validators.required]],
    reason: ['Damaged' as InventoryAdjustmentReason, [Validators.required]],
    quantity: [1, [Validators.required, Validators.min(0.01), this.maxFractionDigits(2)]],
    performedAt: [''],
    notes: [''],
  });

  readonly voidForm = this.formBuilder.nonNullable.group({
    reason: ['', [Validators.required, this.notBlankValidator(), Validators.maxLength(500)]],
  });

  constructor() {
    this.adjustmentForm.controls.direction.valueChanges.subscribe((direction) => {
      this.adjustmentDirectionValue.set(direction);
      const options = direction === 'Increase' ? this.increaseReasonOptions : this.decreaseReasonOptions;
      if (!options.some((option) => option.value === this.adjustmentForm.controls.reason.value)) {
        this.adjustmentForm.controls.reason.setValue(options[0].value);
      }
      this.updateQuantityValidators();
    });

    this.adjustmentForm.controls.reason.valueChanges.subscribe(() => {
      this.updateNotesValidators();
    });

    this.loadHistory();
    this.loadBatches();
  }

  loadHistory(pageNumber = this.pageNumber()): void {
    this.loading.set(true);
    const filterValue = this.filterForm.getRawValue();
    const query: InventoryAdjustmentHistoryQuery = {
      pageNumber,
      pageSize: this.pageSize(),
      itemId: this.nullable(filterValue.itemId),
      batchId: this.nullable(filterValue.batchId),
      direction: filterValue.direction,
      reason: filterValue.reason,
      from: this.nullable(filterValue.from),
      to: this.nullable(filterValue.to),
      includeVoided: filterValue.includeVoided ?? false,
    };

    this.inventoryService
      .getAdjustmentHistory(query)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (response) => {
          this.adjustments.set([...response.items]);
          this.totalCount.set(response.totalCount);
          this.pageNumber.set(response.pageNumber);
          this.pageSize.set(response.pageSize);
        },
        error: () => this.showError('inventory.loadAdjustmentsError'),
      });
  }

  loadBatches(): void {
    this.loadingBatches.set(true);
    this.inventoryService
      .getInventoryBatches()
      .pipe(finalize(() => this.loadingBatches.set(false)))
      .subscribe({
        next: (batches) => this.batches.set([...batches]),
        error: () => this.showError('inventory.loadBatchesError'),
      });
  }

  onApplyFilters(): void {
    this.loadHistory(1);
  }

  onClearFilters(): void {
    this.filterForm.reset({
      itemId: '',
      batchId: '',
      direction: null,
      reason: null,
      from: '',
      to: '',
      includeVoided: false,
    });
    this.loadHistory(1);
  }

  onPageChange(page: number): void {
    if (page < 1) return;
    this.loadHistory(page);
  }

  openNewAdjustment(): void {
    if (!this.canCreateAdjustments()) return;

    this.selectedBatch.set(null);
    this.batchSuggestions.set(this.availableBatches().slice(0, 15));
    this.adjustmentForm.reset({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 1,
      performedAt: '',
      notes: '',
    });
    this.adjustmentDirectionValue.set('Decrease');
    this.updateQuantityValidators();
    this.updateNotesValidators();
    this.isAdjustmentDialogOpen.set(true);
  }

  onBatchSearch(event: AutoCompleteCompleteEvent | { query: string }): void {
    const query = event.query.trim().toLowerCase();
    const matches = this.availableBatches()
      .filter(
        (batch) =>
          batch.itemName.toLowerCase().includes(query) ||
          batch.barcode.toLowerCase().includes(query) ||
          batch.batchNumber.toLowerCase().includes(query),
      )
      .slice(0, 15);
    this.batchSuggestions.set(matches);
  }

  onSelectBatch(batch: InventoryBatchDto): void {
    this.selectedBatch.set(batch);
    this.updateQuantityValidators();
  }

  onSaveAdjustment(): void {
    const batch = this.selectedBatch();
    if (!batch || this.adjustmentForm.invalid || this.saving()) return;

    this.saving.set(true);
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
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.showSuccess('inventory.batchAdjusted');
          this.isAdjustmentDialogOpen.set(false);
          this.loadHistory(1);
          this.loadBatches();
        },
        error: (err) => {
          const detail = err.error?.detail || this.translate('inventory.adjustBatchError');
          this.messageService.add({ severity: 'error', summary: 'Error', detail });
        },
      });
  }

  reasonLabel(reason: InventoryAdjustmentReason): string {
    return (
      [...this.decreaseReasonOptions, ...this.increaseReasonOptions].find(
        (option) => option.value === reason,
      )?.label ?? reason
    );
  }

  directionSeverity(direction: InventoryAdjustmentDirection): 'success' | 'danger' {
    return direction === 'Increase' ? 'success' : 'danger';
  }

  statusSeverity(adjustment: InventoryAdjustmentHistoryItem): 'success' | 'danger' {
    return adjustment.isVoided ? 'danger' : 'success';
  }

  canVoidAdjustment(adjustment: InventoryAdjustmentHistoryItem): boolean {
    return this.canVoidAdjustments() && !adjustment.isVoided;
  }

  onOpenVoidAdjustment(adjustment: InventoryAdjustmentHistoryItem): void {
    if (!this.canVoidAdjustment(adjustment)) return;

    this.selectedAdjustment.set(adjustment);
    this.voidForm.reset({ reason: '' });
    this.isVoidDialogOpen.set(true);
  }

  onSaveVoidAdjustment(): void {
    const adjustment = this.selectedAdjustment();
    if (!adjustment || this.voidForm.invalid || this.voidSaving()) {
      this.voidForm.markAllAsTouched();
      return;
    }

    this.voidSaving.set(true);
    this.inventoryService
      .voidAdjustment(adjustment.adjustmentId, {
        reason: this.voidForm.controls.reason.value.trim(),
      })
      .pipe(finalize(() => this.voidSaving.set(false)))
      .subscribe({
        next: () => {
          this.showSuccess('inventory.adjustmentVoided');
          this.isVoidDialogOpen.set(false);
          this.selectedAdjustment.set(null);
          this.loadHistory(1);
        },
        error: (err) => {
          const detail = err.error?.detail || this.translate('inventory.voidAdjustmentError');
          this.messageService.add({ severity: 'error', summary: 'Error', detail });
        },
      });
  }

  private updateQuantityValidators(): void {
    const validators: ValidatorFn[] = [
      Validators.required,
      Validators.min(0.01),
      this.maxFractionDigits(2),
      this.decreaseBlockedValidator(),
    ];
    const batch = this.selectedBatch();
    if (batch && this.adjustmentForm.controls.direction.value === 'Decrease' && batch.quantity > 0) {
      validators.push(Validators.max(batch.quantity));
    }
    this.adjustmentForm.controls.quantity.setValidators(validators);
    this.adjustmentForm.controls.quantity.updateValueAndValidity({ emitEvent: false });
  }

  private updateNotesValidators(): void {
    const reason = this.adjustmentForm.controls.reason.value;
    const validators =
      reason === 'OtherLoss' || reason === 'OtherGain'
        ? [Validators.required, Validators.maxLength(500)]
        : [Validators.maxLength(500)];
    this.adjustmentForm.controls.notes.setValidators(validators);
    this.adjustmentForm.controls.notes.updateValueAndValidity({ emitEvent: false });
  }

  private decreaseBlockedValidator(): ValidatorFn {
    return (): ValidationErrors | null => {
      const batch = this.selectedBatch();
      return batch &&
        batch.quantity <= 0 &&
        this.adjustmentForm?.controls.direction.value === 'Decrease'
        ? { decreaseFromEmptyBatch: true }
        : null;
    };
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

  private notBlankValidator(): ValidatorFn {
    return (control: AbstractControl<string>): ValidationErrors | null => {
      return control.value.trim().length === 0 ? { required: true } : null;
    };
  }

  private nullable(value: string | null | undefined): string | null {
    const normalized = value?.trim() ?? '';
    return normalized.length > 0 ? normalized : null;
  }

  private toIsoTimestamp(value: string): string | null {
    const normalized = value.trim();
    if (!normalized) return null;
    const date = new Date(normalized);
    return Number.isNaN(date.getTime()) ? normalized : formatUtcIsoInstant(date);
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
}
