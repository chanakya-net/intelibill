import { CommonModule } from '@angular/common';
import {
  Component,
  EventEmitter,
  Output,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { finalize, Subscription } from 'rxjs';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import {
  CreateDiscountRuleRequest,
  DiscountRuleDto,
  DiscountRulePreviewDto,
  DiscountService,
  PreviewDiscountRuleRequest,
  ReplaceDiscountRuleRequest,
} from '../services/discount.service';
import { formatUtcIsoInstant } from '../../../shared/utils/date-time.util';
import {
  DiscountConditions,
  DiscountConditionsFormComponent,
  DiscountEditorMode,
} from './discount-rule-editor/discount-conditions-form.component';
import { DiscountTargetItemsComponent } from './discount-rule-editor/discount-target-items.component';
@Component({
  selector: 'app-discount-rule-editor-dialog',
  standalone: true,
  imports: [
    CommonModule,
    TranslocoPipe,
    ButtonModule,
    CardModule,
    DialogModule,
    DiscountConditionsFormComponent,
    DiscountTargetItemsComponent,
  ],
  templateUrl: './discount-rule-editor-dialog.component.html',
  styleUrl: './discount-rule-editor-dialog.component.scss',
})
export class DiscountRuleEditorDialogComponent {
  private readonly discountService = inject(DiscountService);
  private previewRequest?: Subscription;

  @ViewChild(DiscountConditionsFormComponent)
  private readonly conditionsForm?: DiscountConditionsFormComponent;

  @ViewChild(DiscountTargetItemsComponent)
  private readonly targetItemsForm?: DiscountTargetItemsComponent;

  @Output() readonly saved = new EventEmitter<DiscountRuleDto>();
  @Output() readonly closed = new EventEmitter<void>();

  readonly visible = signal(false);
  readonly mode = signal<DiscountEditorMode>('create');
  readonly editingRule = signal<DiscountRuleDto | null>(null);
  readonly previewLoading = signal(false);
  readonly saveLoading = signal(false);
  readonly submitErrorKey = signal('');
  readonly submitErrorMessage = signal('');
  readonly preview = signal<DiscountRulePreviewDto | null>(null);
  readonly conditions = signal<DiscountConditions>({
    ruleType: 'BatchPercentage',
    name: '',
    description: null,
    percentage: 10,
    thresholdAmount: null,
    startsAt: '',
    endsAt: '',
    belowCostConfirmed: false,
    belowCostConfirmationReason: '',
    disabledReason: '',
  });
  readonly selectedItemIds = signal<readonly string[]>([]);

  readonly titleKey = computed(() =>
    this.mode() === 'create' ? 'discounts.editor.createTitle' : 'discounts.editor.editTitle',
  );
  readonly saveLabelKey = computed(() =>
    this.mode() === 'create'
      ? 'discounts.editor.actions.create'
      : 'discounts.editor.actions.replace',
  );
  readonly isBatchRule = computed(() => this.conditions().ruleType === 'BatchPercentage');
  readonly shouldAskBelowCostReason = computed(
    () => (this.preview()?.belowCostSample.length ?? 0) > 0,
  );
  readonly isBusy = computed(() => this.previewLoading() || this.saveLoading());

  open(mode: DiscountEditorMode, rule: DiscountRuleDto | null = null): void {
    this.previewRequest?.unsubscribe();
    this.mode.set(mode);
    this.editingRule.set(rule);
    this.visible.set(true);
    this.previewLoading.set(false);
    this.saveLoading.set(false);
    this.clearSubmitError();
    this.preview.set(null);

    this.conditions.set(this.toConditions(rule));
    this.selectedItemIds.set(
      rule?.ruleType === 'BatchPercentage' && rule.inventoryBatchId ? [rule.inventoryBatchId] : [],
    );
  }

  close(): void {
    if (this.isBusy() || !this.visible()) return;

    this.visible.set(false);
    this.closed.emit();
  }

  onConditionsChange(next: DiscountConditions): void {
    this.conditions.set(next);
    this.invalidatePreview();
  }

  onTargetSelectionChange(ids: readonly string[]): void {
    this.selectedItemIds.set(ids);
    this.invalidatePreview();
  }

  onPreviewRule(): void {
    if (this.isBusy()) return;
    if (!this.validateFormForPreview()) {
      return;
    }

    this.runPreview(false);
  }

  onSubmit(): void {
    if (this.isBusy()) return;
    if (!this.validateFormForPreview()) {
      return;
    }

    this.runPreview(true);
  }

  private runPreview(continueToSave: boolean): void {
    this.previewLoading.set(true);
    this.clearSubmitError();

    this.previewRequest = this.discountService
      .previewDiscountRule(this.buildPreviewRequest())
      .pipe(finalize(() => this.previewLoading.set(false)))
      .subscribe({
        next: (preview) => this.handlePreview(preview, continueToSave),
        error: () => {
          this.setSubmitErrorKey('discounts.errors.previewFailed');
        },
      });
  }

  private handlePreview(preview: DiscountRulePreviewDto, continueToSave: boolean): void {
    this.preview.set(preview);
    if (preview.errors.length > 0) {
      this.setSubmitErrorMessage(preview.errors[0].message);
      return;
    }
    if (!continueToSave || !this.validateBelowCost(preview)) return;

    this.saveLoading.set(true);
    if (this.mode() === 'create') {
      this.saveCreateRule();
    } else {
      this.saveReplacementRule();
    }
  }

  private validateBelowCost(preview: DiscountRulePreviewDto): boolean {
    if (preview.belowCostSample.length === 0) return true;

    if (!this.conditions().belowCostConfirmed) {
      this.setSubmitErrorKey('discounts.editor.errors.belowCostConfirmationRequired');
      return false;
    }
    if (!this.conditions().belowCostConfirmationReason) {
      this.setSubmitErrorKey('discounts.editor.errors.belowCostReasonRequired');
      return false;
    }
    return true;
  }

  private saveCreateRule(): void {
    this.discountService
      .createDiscountRule(this.buildSaveBase())
      .pipe(finalize(() => this.saveLoading.set(false)))
      .subscribe({
        next: (rule) => this.completeSave(rule),
        error: () => this.setSubmitErrorKey('discounts.errors.createFailed'),
      });
  }

  private saveReplacementRule(): void {
    const editingRule = this.editingRule();
    if (!editingRule) {
      this.saveLoading.set(false);
      return;
    }
    this.discountService
      .replaceDiscountRule(editingRule.id, this.buildReplaceRequest())
      .pipe(finalize(() => this.saveLoading.set(false)))
      .subscribe({
        next: (rule) => this.completeSave(rule),
        error: () => this.setSubmitErrorKey('discounts.errors.replaceFailed'),
      });
  }

  private completeSave(rule: DiscountRuleDto): void {
    this.previewLoading.set(false);
    this.saveLoading.set(false);
    this.saved.emit(rule);
    this.close();
  }

  private invalidatePreview(): void {
    this.previewRequest?.unsubscribe();
    this.previewRequest = undefined;
    this.preview.set(null);
    this.clearSubmitError();
  }

  private validateFormForPreview(): boolean {
    const conditionsValid = this.conditionsForm?.isValid() ?? this.isConditionsSignalValid();
    const targetValid =
      !this.isBatchRule() || (this.targetItemsForm?.isValid() ?? this.selectedItemIds().length > 0);

    if (!conditionsValid || !targetValid) {
      this.conditionsForm?.markAllAsTouched();
      this.targetItemsForm?.markAllAsTouched();
      this.setSubmitErrorKey('discounts.editor.errors.fixValidation');
      return false;
    }

    return true;
  }

  private isConditionsSignalValid(): boolean {
    const conditions = this.conditions();
    const hasName = conditions.name.trim().length > 0;
    const isPercentageValid = conditions.percentage > 0 && conditions.percentage <= 100;
    const thresholdAmount = conditions.thresholdAmount;
    const isThresholdRule = conditions.ruleType === 'SaleThresholdPercentage';
    const isThresholdAmountValid = !isThresholdRule || (!!thresholdAmount && thresholdAmount > 0);

    return hasName && conditions.name.length <= 200 && isPercentageValid && isThresholdAmountValid;
  }

  private buildPreviewRequest(): PreviewDiscountRuleRequest {
    const conditions = this.conditions();

    return {
      ruleType: conditions.ruleType,
      percentage: conditions.percentage,
      thresholdAmount:
        conditions.ruleType === 'SaleThresholdPercentage' ? conditions.thresholdAmount : null,
      inventoryBatchId: this.isBatchRule() ? (this.selectedItemIds()[0] ?? null) : null,
      startsAt: this.toOutputValue(conditions.startsAt),
      endsAt: this.toOutputValue(conditions.endsAt),
      belowCostConfirmed: conditions.belowCostConfirmed,
    };
  }

  private buildReplaceRequest(): ReplaceDiscountRuleRequest {
    return {
      ...this.buildSaveBase(),
      disabledReason: this.emptyStringToNull(this.conditions().disabledReason),
    };
  }

  private buildSaveBase(): CreateDiscountRuleRequest {
    const conditions = this.conditions();

    return {
      ruleType: conditions.ruleType,
      name: conditions.name,
      description: conditions.description,
      inventoryBatchId: this.isBatchRule() ? (this.selectedItemIds()[0] ?? null) : null,
      percentage: conditions.percentage,
      thresholdAmount:
        conditions.ruleType === 'SaleThresholdPercentage' ? conditions.thresholdAmount : null,
      startsAt: this.toOutputValue(conditions.startsAt),
      endsAt: this.toOutputValue(conditions.endsAt),
      belowCostConfirmed: conditions.belowCostConfirmed,
      belowCostConfirmationReason: this.emptyStringToNull(conditions.belowCostConfirmationReason),
    };
  }

  private toConditions(rule: DiscountRuleDto | null): DiscountConditions {
    return {
      ruleType: rule?.ruleType ?? 'BatchPercentage',
      name: rule?.name ?? '',
      description: rule?.description ?? null,
      percentage: rule?.percentage ?? 10,
      thresholdAmount: rule?.thresholdAmount ?? null,
      startsAt: this.toInputValue(rule?.startsAt ?? null),
      endsAt: this.toInputValue(rule?.endsAt ?? null),
      belowCostConfirmed: rule?.belowCostConfirmed ?? false,
      belowCostConfirmationReason: rule?.belowCostConfirmationReason ?? '',
      disabledReason: rule?.disabledReason ?? '',
    };
  }

  private emptyStringToNull(value: string | null | undefined): string | null {
    const next = value?.trim();
    return next === '' || next === undefined ? null : next;
  }

  private toInputValue(value: string | null): string {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return value;
    const pad = (p: number) => String(p).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  private toOutputValue(value: string | null): string | null {
    if (!value) return null;
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return value;
    return formatUtcIsoInstant(date);
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
