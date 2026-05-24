import { Component, DestroyRef, EventEmitter, Input, Output, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';

import { DiscountRuleType } from '../../services/discount.service';

export type DiscountEditorMode = 'create' | 'edit';

export interface DiscountConditions {
  readonly ruleType: DiscountRuleType;
  readonly name: string;
  readonly description: string | null;
  readonly percentage: number;
  readonly thresholdAmount: number | null;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly belowCostConfirmed: boolean;
  readonly belowCostConfirmationReason: string;
  readonly disabledReason: string;
}

interface SelectOption<T> {
  readonly label: string;
  readonly value: T;
}

const createDefaultConditions = (): DiscountConditions => ({
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

@Component({
  selector: 'app-discount-conditions-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslocoPipe, SelectModule, InputNumberModule, InputTextModule, TextareaModule],
  templateUrl: './discount-conditions-form.component.html',
})
export class DiscountConditionsFormComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private suppressConditionEmit = false;

  @Input() set initialConditions(value: DiscountConditions | null) {
    this.applyInitialConditions(value ?? createDefaultConditions());
  }

  @Input() mode: DiscountEditorMode = 'create';

  @Input() shouldShowBelowCostReason = false;

  @Output() readonly conditionsChange = new EventEmitter<DiscountConditions>();

  readonly form = this.formBuilder.nonNullable.group({
    ruleType: this.formBuilder.nonNullable.control<DiscountRuleType>('BatchPercentage', [Validators.required]),
    name: this.formBuilder.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    description: this.formBuilder.nonNullable.control(''),
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

  readonly isBatchRule = computed(() => this.form.controls.ruleType.value === 'BatchPercentage');
  readonly isThresholdRule = computed(() => this.form.controls.ruleType.value === 'SaleThresholdPercentage');

  constructor() {
    this.form.controls.ruleType.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.syncDynamicValidators());

    this.form.controls.belowCostConfirmed.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.syncDynamicValidators());

    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.emitConditions());
    this.syncDynamicValidators();
  }

  isValid(): boolean {
    return this.form.valid;
  }

  markAllAsTouched(): void {
    this.form.markAllAsTouched();
  }

  private applyInitialConditions(next: DiscountConditions): void {
    this.suppressConditionEmit = true;
    this.form.reset(
      {
        ruleType: next.ruleType,
        name: next.name,
        description: next.description ?? '',
        percentage: next.percentage,
        thresholdAmount: next.thresholdAmount,
        startsAt: next.startsAt,
        endsAt: next.endsAt,
        belowCostConfirmed: next.belowCostConfirmed,
        belowCostConfirmationReason: next.belowCostConfirmationReason,
        disabledReason: next.disabledReason,
      },
      { emitEvent: false },
    );
    this.suppressConditionEmit = false;
    this.syncDynamicValidators();
  }

  private emitConditions(): void {
    if (this.suppressConditionEmit) {
      return;
    }
    this.conditionsChange.emit(this.buildConditions());
  }

  private buildConditions(): DiscountConditions {
    const name = this.form.controls.name.value.trim();
    const description = this.form.controls.description.value.trim();
    const startsAt = this.form.controls.startsAt.value.trim();
    const endsAt = this.form.controls.endsAt.value.trim();

    return {
      ruleType: this.form.controls.ruleType.value,
      name,
      description: description.length > 0 ? description : null,
      percentage: this.form.controls.percentage.value,
      thresholdAmount: this.form.controls.thresholdAmount.value,
      startsAt,
      endsAt,
      belowCostConfirmed: this.form.controls.belowCostConfirmed.value,
      belowCostConfirmationReason: this.form.controls.belowCostConfirmationReason.value.trim(),
      disabledReason: this.form.controls.disabledReason.value.trim(),
    };
  }

  private syncDynamicValidators(): void {
    const thresholdAmount = this.form.controls.thresholdAmount;
    const confirmationReason = this.form.controls.belowCostConfirmationReason;

    if (this.form.controls.ruleType.value === 'SaleThresholdPercentage') {
      thresholdAmount.setValidators([Validators.required, Validators.min(0.01)]);
    } else {
      thresholdAmount.clearValidators();
    }

    confirmationReason.setValidators([Validators.maxLength(500)]);

    thresholdAmount.updateValueAndValidity({ emitEvent: false });
    confirmationReason.updateValueAndValidity({ emitEvent: false });
  }
}
