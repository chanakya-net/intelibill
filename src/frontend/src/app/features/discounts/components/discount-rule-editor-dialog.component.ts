import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnDestroy, Output, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { EMPTY, Subject, debounceTime, finalize, switchMap, takeUntil } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';

import { InventoryService, AvailableBatchDto } from '../../inventory/services/inventory.service';
import {
  CreateDiscountRuleRequest,
  DiscountRuleDto,
  DiscountRulePreviewDto,
  DiscountRuleType,
  DiscountService,
  PreviewDiscountRuleRequest,
  ReplaceDiscountRuleRequest,
} from '../services/discount.service';

type DiscountEditorMode = 'create' | 'edit';

interface SelectOption<T> {
  readonly label: string;
  readonly value: T;
}

@Component({
  selector: 'app-discount-rule-editor-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslocoPipe,
    ButtonModule,
    CardModule,
    DialogModule,
    InputTextModule,
    InputNumberModule,
    SelectModule,
    TagModule,
    TextareaModule,
  ],
  templateUrl: './discount-rule-editor-dialog.component.html',
  styleUrl: './discount-rule-editor-dialog.component.scss',
})
export class DiscountRuleEditorDialogComponent implements OnDestroy {
  private readonly discountService = inject(DiscountService);
  private readonly inventoryService = inject(InventoryService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroy$ = new Subject<void>();
  private readonly batchSearch$ = new Subject<string>();

  @Output() readonly saved = new EventEmitter<DiscountRuleDto>();
  @Output() readonly closed = new EventEmitter<void>();

  readonly visible = signal(false);
  readonly mode = signal<DiscountEditorMode>('create');
  readonly editingRule = signal<DiscountRuleDto | null>(null);
  readonly batchSearchTerm = signal('');
  readonly batchSearchResults = signal<readonly AvailableBatchDto[]>([]);
  readonly batchSearchNoResults = signal(false);
  readonly selectedBatchLabel = signal('');
  readonly searchLoading = signal(false);
  readonly previewLoading = signal(false);
  readonly saveLoading = signal(false);
  readonly submitErrorKey = signal('');
  readonly submitErrorMessage = signal('');
  readonly preview = signal<DiscountRulePreviewDto | null>(null);

  readonly batchSearchMinCharsRequired = computed(() => this.batchSearchTerm().trim().length < 3);

  readonly form = this.formBuilder.group({
    ruleType: this.formBuilder.nonNullable.control<DiscountRuleType>('BatchPercentage', [
      Validators.required,
    ]),
    name: this.formBuilder.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    description: this.formBuilder.nonNullable.control(''),
    inventoryBatchId: this.formBuilder.nonNullable.control('', [Validators.maxLength(64)]),
    percentage: this.formBuilder.nonNullable.control(10, [
      Validators.required,
      Validators.min(0.01),
      Validators.max(100),
    ]),
    thresholdAmount: this.formBuilder.control<number | null>(null),
    startsAt: this.formBuilder.nonNullable.control(''),
    endsAt: this.formBuilder.nonNullable.control(''),
    belowCostConfirmed: this.formBuilder.nonNullable.control(false),
    belowCostConfirmationReason: this.formBuilder.nonNullable.control(''),
    disabledReason: this.formBuilder.nonNullable.control(''),
  });

  readonly ruleTypeOptions: SelectOption<DiscountRuleType>[] = [
    { label: 'discounts.editor.ruleType.batchPercentage', value: 'BatchPercentage' },
    { label: 'discounts.editor.ruleType.salePercentage', value: 'SalePercentage' },
    { label: 'discounts.editor.ruleType.saleThresholdPercentage', value: 'SaleThresholdPercentage' },
  ];

  readonly titleKey = computed(() =>
    this.mode() === 'create' ? 'discounts.editor.createTitle' : 'discounts.editor.editTitle',
  );

  readonly saveLabelKey = computed(() =>
    this.mode() === 'create' ? 'discounts.editor.actions.create' : 'discounts.editor.actions.replace',
  );

  readonly isBatchRule = computed(() => this.form.controls.ruleType.value === 'BatchPercentage');
  readonly isThresholdRule = computed(() => this.form.controls.ruleType.value === 'SaleThresholdPercentage');
  readonly shouldAskBelowCostReason = computed(() => (this.preview()?.belowCostSample.length ?? 0) > 0);

  constructor() {
    this.form.controls.ruleType.valueChanges.subscribe(() => this.syncDynamicValidators());
    this.form.controls.belowCostConfirmed.valueChanges.subscribe(() => this.syncDynamicValidators());
    this.syncDynamicValidators();

    // Setup debounced batch search
    this.batchSearch$
      .pipe(
        debounceTime(300),
        switchMap((searchTerm) => {
          const trimmed = searchTerm.trim();
          if (trimmed.length < 3) {
            this.batchSearchResults.set([]);
            this.batchSearchNoResults.set(false);
            this.searchLoading.set(false);
            return EMPTY;
          }

          this.searchLoading.set(true);
          this.batchSearchNoResults.set(false);

          this.form.controls.inventoryBatchId.setValue('');
          this.selectedBatchLabel.set('');

          return this.inventoryService.getAvailableBatchesBySearchTerm(trimmed).pipe(
            finalize(() => this.searchLoading.set(false)),
            takeUntil(this.destroy$),
          );
        }),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: (batches) => {
          if (batches.length === 1) {
            this.batchSearchResults.set([]);
            this.batchSearchNoResults.set(false);
            this.onSelectBatch(batches[0]);
            return;
          }

          if (batches.length === 0) {
            this.batchSearchResults.set([]);
            this.batchSearchNoResults.set(true);
            this.form.controls.inventoryBatchId.setValue('');
            this.selectedBatchLabel.set('');
            return;
          }

          this.batchSearchResults.set(batches);
          this.batchSearchNoResults.set(false);
          this.clearSubmitError();
        },
        error: () => {
          this.batchSearchResults.set([]);
          this.batchSearchNoResults.set(false);
          this.setSubmitErrorKey('discounts.errors.batchSearchFailed');
        },
      });

    // Debounced stream handles search term updates via onBatchSearchTermChange.
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  open(mode: DiscountEditorMode, rule: DiscountRuleDto | null = null): void {
    this.mode.set(mode);
    this.editingRule.set(rule);
    this.visible.set(true);
    this.clearSubmitError();
    this.preview.set(null);
    this.batchSearchResults.set([]);
    this.batchSearchTerm.set('');
    this.selectedBatchLabel.set('');
    this.batchSearchNoResults.set(false);

    this.form.reset({
      ruleType: rule?.ruleType ?? 'BatchPercentage',
      name: rule?.name ?? '',
      description: rule?.description ?? '',
      inventoryBatchId: rule?.inventoryBatchId ?? '',
      percentage: rule?.percentage ?? 10,
      thresholdAmount: rule?.thresholdAmount ?? null,
      startsAt: this.toInputValue(rule?.startsAt ?? null),
      endsAt: this.toInputValue(rule?.endsAt ?? null),
      belowCostConfirmed: rule?.belowCostConfirmed ?? false,
      belowCostConfirmationReason: rule?.belowCostConfirmationReason ?? '',
      disabledReason: rule?.disabledReason ?? '',
    });

    if (rule?.inventoryBatchId) {
      this.batchSearchTerm.set(rule.inventoryBatchId);
      this.selectedBatchLabel.set(rule.inventoryBatchId);
    }

    this.syncDynamicValidators();
  }

  close(): void {
    if (this.saveLoading()) {
      return;
    }

    this.visible.set(false);
    this.closed.emit();
  }

  onBatchSearchTermChange(rawValue: string): void {
    const searchTerm = rawValue;
    const trimmed = searchTerm.trim();
    const selectedLabel = this.selectedBatchLabel();

    this.batchSearchTerm.set(searchTerm);

    if (selectedLabel && trimmed !== selectedLabel.trim()) {
      this.form.controls.inventoryBatchId.setValue('');
      this.selectedBatchLabel.set('');
    }

    if (!selectedLabel) {
      this.form.controls.inventoryBatchId.setValue('');
    }

    this.batchSearch$.next(searchTerm);
  }

  onSelectBatch(batch: AvailableBatchDto): void {
    this.form.controls.inventoryBatchId.setValue(batch.inventoryBatchId);
    this.batchSearchTerm.set(`${batch.itemName} · ${batch.batchNumber}`);
    this.selectedBatchLabel.set(`${batch.itemName} · ${batch.batchNumber}`);
    this.batchSearchResults.set([]);
    this.batchSearchNoResults.set(false);
  }

  onPreviewRule(): void {
    if (!this.validateFormForPreview()) {
      return;
    }

    this.runPreview(false);
  }

  onSubmit(): void {
    if (!this.validateFormForPreview()) {
      return;
    }

    this.runPreview(true);
  }

  private runPreview(continueToSave: boolean): void {
    this.previewLoading.set(true);
    this.clearSubmitError();

    this.discountService
      .previewDiscountRule(this.buildPreviewRequest())
      .pipe(finalize(() => this.previewLoading.set(false)))
      .subscribe({
        next: (preview) => {
          this.preview.set(preview);

          if (preview.errors.length > 0) {
            this.setSubmitErrorMessage(preview.errors[0].message);
            return;
          }

          if (!continueToSave) {
            return;
          }

          if (preview.belowCostSample.length > 0 && !this.form.controls.belowCostConfirmed.value) {
            this.setSubmitErrorKey('discounts.editor.errors.belowCostConfirmationRequired');
            return;
          }

          if (
            preview.belowCostSample.length > 0 &&
            this.form.controls.belowCostConfirmed.value &&
            !this.form.controls.belowCostConfirmationReason.value.trim()
          ) {
            this.setSubmitErrorKey('discounts.editor.errors.belowCostReasonRequired');
            return;
          }

          this.saveLoading.set(true);
          if (this.mode() === 'create') {
            this.discountService
              .createDiscountRule(this.buildCreateRequest())
              .pipe(finalize(() => this.saveLoading.set(false)))
              .subscribe({
                next: (rule) => {
                  this.saved.emit(rule);
                  this.close();
                },
                error: () => {
                  this.setSubmitErrorKey('discounts.errors.createFailed');
                },
              });
            return;
          }

          this.discountService
            .replaceDiscountRule(this.editingRule()!.id, this.buildReplaceRequest())
            .pipe(finalize(() => this.saveLoading.set(false)))
            .subscribe({
              next: (rule) => {
                this.saved.emit(rule);
                this.close();
              },
              error: () => {
                this.setSubmitErrorKey('discounts.errors.replaceFailed');
              },
            });
        },
        error: () => {
          this.setSubmitErrorKey('discounts.errors.previewFailed');
        },
      });
  }

  private validateFormForPreview(): boolean {
    this.syncDynamicValidators();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.setSubmitErrorKey('discounts.editor.errors.fixValidation');
      return false;
    }

    return true;
  }

  private buildPreviewRequest(): PreviewDiscountRuleRequest {
    return {
      ruleType: this.form.controls.ruleType.value,
      percentage: this.form.controls.percentage.value,
      thresholdAmount:
        this.form.controls.ruleType.value === 'SaleThresholdPercentage'
          ? this.form.controls.thresholdAmount.value
          : null,
      inventoryBatchId:
        this.form.controls.ruleType.value === 'BatchPercentage'
          ? this.emptyStringToNull(this.form.controls.inventoryBatchId.value)
          : null,
      startsAt: this.toOutputValue(this.form.controls.startsAt.value),
      endsAt: this.toOutputValue(this.form.controls.endsAt.value),
      belowCostConfirmed: this.form.controls.belowCostConfirmed.value,
    };
  }

  private buildCreateRequest(): CreateDiscountRuleRequest {
    return this.buildSaveBase();
  }

  private buildReplaceRequest(): ReplaceDiscountRuleRequest {
    return {
      ...this.buildSaveBase(),
      disabledReason: this.emptyStringToNull(this.form.controls.disabledReason.value),
    };
  }

  private buildSaveBase(): CreateDiscountRuleRequest {
    return {
      ruleType: this.form.controls.ruleType.value,
      name: this.form.controls.name.value.trim(),
      description: this.emptyStringToNull(this.form.controls.description.value),
      inventoryBatchId:
        this.form.controls.ruleType.value === 'BatchPercentage'
          ? this.emptyStringToNull(this.form.controls.inventoryBatchId.value)
          : null,
      percentage: this.form.controls.percentage.value,
      thresholdAmount:
        this.form.controls.ruleType.value === 'SaleThresholdPercentage'
          ? this.form.controls.thresholdAmount.value
          : null,
      startsAt: this.toOutputValue(this.form.controls.startsAt.value),
      endsAt: this.toOutputValue(this.form.controls.endsAt.value),
      belowCostConfirmed: this.form.controls.belowCostConfirmed.value,
      belowCostConfirmationReason: this.emptyStringToNull(
        this.form.controls.belowCostConfirmationReason.value,
      ),
  };
  }

  private syncDynamicValidators(): void {
    const batchId = this.form.controls.inventoryBatchId;
    const thresholdAmount = this.form.controls.thresholdAmount;
    const confirmationReason = this.form.controls.belowCostConfirmationReason;

    if (this.form.controls.ruleType.value === 'BatchPercentage') {
      batchId.setValidators([Validators.required, Validators.maxLength(64)]);
    } else {
      batchId.clearValidators();
    }

    if (this.form.controls.ruleType.value === 'SaleThresholdPercentage') {
      thresholdAmount.setValidators([Validators.required, Validators.min(0.01)]);
    } else {
      thresholdAmount.clearValidators();
    }

    confirmationReason.setValidators([Validators.maxLength(500)]);

    batchId.updateValueAndValidity({ emitEvent: false });
    thresholdAmount.updateValueAndValidity({ emitEvent: false });
    confirmationReason.updateValueAndValidity({ emitEvent: false });
  }

  private emptyStringToNull(value: string): string | null {
    return value.trim() === '' ? null : value;
  }

  private toInputValue(value: string | null): string {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return value;
    const pad = (part: number): string => String(part).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(
      date.getHours(),
    )}:${pad(date.getMinutes())}`;
  }

  private toOutputValue(value: string): string | null {
    const trimmed = value.trim();
    if (!trimmed) return null;
    const date = new Date(trimmed);
    if (Number.isNaN(date.getTime())) return trimmed;
    return date.toISOString();
  }

  private clearSubmitError(): void {
    this.submitErrorKey.set('');
    this.submitErrorMessage.set('');
  }

  private setSubmitErrorKey(key: string): void {
    this.submitErrorKey.set(key);
    this.submitErrorMessage.set('');
  }

  private setSubmitErrorMessage(message: string): void {
    this.submitErrorKey.set('');
    this.submitErrorMessage.set(message);
  }
}
