import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { CardModule } from 'primeng/card';
import { DatePickerModule } from 'primeng/datepicker';
import { InputTextModule } from 'primeng/inputtext';
import { PaginatorModule, PaginatorState } from 'primeng/paginator';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';

import { finalize } from 'rxjs';

import { formatLocalIsoDate, parseDateOnlyAsLocalDate } from '../../../../shared/utils/date-time.util';
import {
  AdjustmentRowDto,
  InventoryAdjustmentDirection,
  InventoryAdjustmentHistoryItem,
  InventoryAdjustmentHistoryQuery,
  InventoryAdjustmentReason,
  InventoryBatchDto,
  InventoryBatchOption,
} from '../../services/inventory.models';
import { InventoryService } from '../../services/inventory.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { AdjustmentRowFormComponent } from '../../components/adjustments/adjustment-row-form.component';
import { AdjustmentSummaryComponent } from '../../components/adjustments/adjustment-summary.component';

interface SelectOption<T extends string | boolean> {
  readonly label: string;
  readonly value: T;
}

@Component({
  selector: 'app-inventory-adjustments-page',
  standalone: true,
  imports: [
    CommonModule,
    DatePipe,
    DecimalPipe,
    FormsModule,
    ReactiveFormsModule,
    TranslocoPipe,
    ButtonModule,
    CheckboxModule,
    DialogModule,
    CardModule,
    DatePickerModule,
    InputTextModule,
    PaginatorModule,
    ProgressSpinnerModule,
    SelectModule,
    TableModule,
    TextareaModule,
    ToastModule,
    AdjustmentRowFormComponent,
    AdjustmentSummaryComponent,
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
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly voidSaving = signal(false);
  readonly isAdjustmentDialogOpen = signal(false);
  readonly isVoidDialogOpen = signal(false);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly selectedAdjustment = signal<InventoryAdjustmentHistoryItem | null>(null);
  readonly fromDateValue = signal<Date | null>(null);
  readonly toDateValue = signal<Date | null>(null);
  readonly adjustmentSummaryRows = computed(() => this.adjustments().map((adjustment) => ({
    batchId: adjustment.batchId,
    direction: adjustment.direction,
    reason: adjustment.reason,
    quantity: adjustment.quantity,
    performedAt: adjustment.performedAt,
    notes: adjustment.notes,
  })));

  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) return '';
    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canCreateAdjustments = computed(() => ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()));
  readonly canVoidAdjustments = computed(() => this.activeShopRole().toLowerCase() === 'owner');

  readonly availableBatches = computed(() => this.batches().filter((batch) => !batch.isVoided));
  readonly totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize()));

  readonly adjustmentBatchOptions = computed<InventoryBatchOption[]>(() =>
    this.availableBatches().map((batch) => ({
      id: batch.id,
      label: `${batch.batchNumber} · ${batch.itemName}`,
      itemName: batch.itemName,
      batchNumber: batch.batchNumber,
      barcode: batch.barcode,
      quantity: batch.quantity,
    })),
  );

  readonly itemOptions = computed(() => {
    const seen = new Map<string, SelectOption<string>>();
    for (const batch of this.availableBatches()) seen.set(batch.itemId, { label: batch.itemName, value: batch.itemId });
    return [...seen.values()].sort((a, b) => a.label.localeCompare(b.label));
  });

  readonly batchOptions = computed(() =>
    this.availableBatches()
      .map((batch) => ({ label: `${batch.batchNumber} · ${batch.itemName}`, value: batch.id }))
      .sort((a, b) => a.label.localeCompare(b.label)),
  );

  readonly reasonOptionsByDirection: { Decrease: SelectOption<InventoryAdjustmentReason>[]; Increase: SelectOption<InventoryAdjustmentReason>[] } = {
    Decrease: [
      { label: this.translate('inventory.adjustmentReason.damaged'), value: 'Damaged' }, { label: this.translate('inventory.adjustmentReason.expired'), value: 'Expired' }, { label: this.translate('inventory.adjustmentReason.stolen'), value: 'Stolen' }, { label: this.translate('inventory.adjustmentReason.missingLost'), value: 'MissingLost' }, { label: this.translate('inventory.adjustmentReason.stockCountCorrection'), value: 'StockCountCorrection' }, { label: this.translate('inventory.adjustmentReason.otherLoss'), value: 'OtherLoss' },
    ],
    Increase: [
      { label: this.translate('inventory.adjustmentReason.foundStock'), value: 'FoundStock' }, { label: this.translate('inventory.adjustmentReason.stockCountCorrection'), value: 'StockCountCorrection' }, { label: this.translate('inventory.adjustmentReason.returnRestockCorrection'), value: 'ReturnRestockCorrection' }, { label: this.translate('inventory.adjustmentReason.otherGain'), value: 'OtherGain' },
    ],
  };

  readonly directionOptions = signal<SelectOption<InventoryAdjustmentDirection>[]>([
    { label: this.translate('inventory.adjustmentDirection.decrease'), value: 'Decrease' },
    { label: this.translate('inventory.adjustmentDirection.increase'), value: 'Increase' },
  ]);

  readonly reasonOptions = computed<SelectOption<InventoryAdjustmentReason>[]>(() => {
    const direction = this.filterForm.controls.direction.value;
    return direction === 'Increase' ? this.reasonOptionsByDirection.Increase : this.reasonOptionsByDirection.Decrease;
  });

  readonly filterForm = this.formBuilder.group({
    itemId: [''],
    batchId: [''],
    direction: [null as InventoryAdjustmentDirection | null],
    reason: [null as InventoryAdjustmentReason | null],
    from: [''],
    to: [''],
    includeVoided: [false],
  });

  readonly voidForm = this.formBuilder.nonNullable.group({
    reason: ['', [Validators.required, (control: AbstractControl<string>) => (control.value.trim().length === 0 ? { required: true } : null), Validators.maxLength(500)]],
  });

  constructor() {
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
        error: () =>
          this.messageService.add({ severity: 'error', summary: this.translocoService.translate('inventory.loadAdjustmentsError'), life: 3500 }),
      });
  }

  loadBatches(): void {
    this.inventoryService.getInventoryBatches().subscribe({
      next: (batches) => this.batches.set([...batches]),
      error: () =>
        this.messageService.add({ severity: 'error', summary: this.translocoService.translate('inventory.loadBatchesError'), life: 3500 }),
    });
  }

  onApplyFilters(): void {
    this.loadHistory(1);
  }

  onClearFilters(): void {
    this.filterForm.reset({ itemId: '', batchId: '', direction: null, reason: null, from: '', to: '', includeVoided: false });
    this.fromDateValue.set(null);
    this.toDateValue.set(null);
    this.loadHistory(1);
  }

  onFromDateChange(value: Date | null): void {
    this.fromDateValue.set(value);
    this.filterForm.patchValue({ from: value ? formatLocalIsoDate(value) : '' });
  }

  onToDateChange(value: Date | null): void {
    this.toDateValue.set(value);
    this.filterForm.patchValue({ to: value ? formatLocalIsoDate(value) : '' });
  }

  onPageChange(page: number): void {
    if (page < 1) return;
    this.loadHistory(page);
  }

  onPaginatorChange(event: PaginatorState): void {
    const nextPage = Math.floor((event.first ?? 0) / (event.rows ?? this.pageSize())) + 1;
    this.onPageChange(nextPage);
  }

  reasonLabel(reason: InventoryAdjustmentReason): string {
    const option = [...this.reasonOptionsByDirection.Decrease, ...this.reasonOptionsByDirection.Increase]
      .find((entry) => entry.value === reason);
    return option?.label ?? reason;
  }

  openNewAdjustment(): void {
    if (!this.canCreateAdjustments()) return;
    this.isAdjustmentDialogOpen.set(true);
  }

  onSaveAdjustment({ batchId, ...payload }: AdjustmentRowDto): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.inventoryService
      .adjustInventoryBatch(batchId, payload)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: this.translocoService.translate('inventory.batchAdjusted'),
            life: 3000,
          });
          this.isAdjustmentDialogOpen.set(false);
          this.loadHistory(1);
          this.loadBatches();
        },
        error: (err) => {
          const detail = err.error?.detail || this.translocoService.translate('inventory.adjustBatchError');
          this.messageService.add({ severity: 'error', summary: 'Error', detail });
        },
      });
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
          this.messageService.add({
            severity: 'success',
            summary: this.translocoService.translate('inventory.adjustmentVoided'),
            life: 3000,
          });
          this.isVoidDialogOpen.set(false);
          this.selectedAdjustment.set(null);
          this.loadHistory(1);
        },
        error: (err) => {
          const detail = err.error?.detail || this.translocoService.translate('inventory.voidAdjustmentError');
          this.messageService.add({ severity: 'error', summary: 'Error', detail });
        },
      });
  }

  private nullable(value: string | null | undefined): string | null {
    const normalized = value?.trim() ?? '';
    return normalized.length > 0 ? normalized : null;
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }
}
