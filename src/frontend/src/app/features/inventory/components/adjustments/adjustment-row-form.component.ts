import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';

import {
  AdjustmentRowDto,
  InventoryAdjustmentDirection,
  InventoryAdjustmentReason,
  InventoryBatchOption,
} from '../../services/inventory.models';
import { formatUtcIsoInstant } from '../../../../shared/utils/date-time.util';
import { InputValidators } from '../../../../shared/forms/input-validation';

interface SelectOption<T extends string> {
  readonly label: string;
  readonly value: T;
}

@Component({
  selector: 'app-adjustment-row-form',
  standalone: true,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslocoPipe,
    AutoCompleteModule,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    InputNumberModule,
    SelectModule,
    TextareaModule,
  ],
  templateUrl: './adjustment-row-form.component.html',
  styleUrl: './adjustment-row-form.component.scss',
})
export class AdjustmentRowFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly translocoService = inject(TranslocoService);

  @Input() batchOptions: InventoryBatchOption[] = [];
  @Input() saving = false;
  @Output() readonly rowChange = new EventEmitter<AdjustmentRowDto>();
  @Output() readonly cancelled = new EventEmitter<void>();

  readonly batchSuggestions = signal<InventoryBatchOption[]>([]);
  readonly selectedBatch = signal<InventoryBatchOption | null>(null);
  private readonly adjustmentDirectionValue = signal<InventoryAdjustmentDirection>('Decrease');

  readonly selectedBatchDecreaseBlocked = computed(() => {
    const batch = this.selectedBatch();
    return !!batch && batch.quantity <= 0 && this.adjustmentDirectionValue() === 'Decrease';
  });

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

  readonly directionOptions = signal<SelectOption<InventoryAdjustmentDirection>[]>([
    { label: this.translate('inventory.adjustmentDirection.decrease'), value: 'Decrease' },
    { label: this.translate('inventory.adjustmentDirection.increase'), value: 'Increase' },
  ]);

  readonly adjustmentReasonOptions = computed<SelectOption<InventoryAdjustmentReason>[]>(() =>
    this.adjustmentDirectionValue() === 'Increase'
      ? this.increaseReasonOptions
      : this.decreaseReasonOptions,
  );

  readonly adjustmentForm = this.formBuilder.nonNullable.group({
    direction: ['Decrease' as InventoryAdjustmentDirection, [Validators.required]],
    reason: ['Damaged' as InventoryAdjustmentReason, [Validators.required]],
    quantity: [
      1,
      [Validators.required, Validators.min(0.01), InputValidators.maxFractionDigits(2)],
    ],
    performedAt: [null as Date | null],
    notes: [''],
  });

  ngOnInit(): void {
    this.batchSuggestions.set(this.batchOptions.slice(0, 15));

    this.adjustmentForm.controls.direction.valueChanges.subscribe((direction) => {
      this.adjustmentDirectionValue.set(direction);
      const options =
        direction === 'Increase' ? this.increaseReasonOptions : this.decreaseReasonOptions;
      if (!options.some((option) => option.value === this.adjustmentForm.controls.reason.value)) {
        this.adjustmentForm.controls.reason.setValue(options[0].value);
      }
      this.updateQuantityValidators();
    });

    this.adjustmentForm.controls.reason.valueChanges.subscribe(() => {
      this.updateNotesValidators();
    });

    this.updateQuantityValidators();
    this.updateNotesValidators();
  }

  onBatchSearch(event: AutoCompleteCompleteEvent | { query: string }): void {
    const query = event.query.trim().toLowerCase();
    const matches = this.batchOptions
      .filter(
        (opt) =>
          opt.itemName.toLowerCase().includes(query) ||
          opt.barcode.toLowerCase().includes(query) ||
          opt.batchNumber.toLowerCase().includes(query),
      )
      .slice(0, 15);
    this.batchSuggestions.set(matches);
  }

  onSelectBatch(batch: InventoryBatchOption): void {
    this.selectedBatch.set(batch);
    this.updateQuantityValidators();
  }

  onBatchModelChange(value: InventoryBatchOption | string | null): void {
    if (value && typeof value === 'object' && 'id' in value) {
      this.onSelectBatch(value);
      return;
    }

    if (value === null || value === '') {
      this.selectedBatch.set(null);
      this.updateQuantityValidators();
    }
  }

  onSave(): void {
    const batch = this.selectedBatch();
    if (!batch || this.adjustmentForm.invalid || this.saving) return;

    const formValue = this.adjustmentForm.getRawValue();
    const dto: AdjustmentRowDto = {
      batchId: batch.id,
      direction: formValue.direction,
      reason: formValue.reason,
      quantity: formValue.quantity,
      performedAt: this.toIsoTimestamp(formValue.performedAt),
      notes: this.nullable(formValue.notes),
    };

    this.rowChange.emit(dto);
  }

  onCancel(): void {
    this.cancelled.emit();
  }

  private updateQuantityValidators(): void {
    const validators: ValidatorFn[] = [
      Validators.required,
      Validators.min(0.01),
      InputValidators.maxFractionDigits(2),
      this.decreaseBlockedValidator(),
    ];
    const batch = this.selectedBatch();
    if (
      batch &&
      this.adjustmentForm.controls.direction.value === 'Decrease' &&
      batch.quantity > 0
    ) {
      validators.push(Validators.max(batch.quantity));
    }
    this.adjustmentForm.controls.quantity.setValidators(validators);
    this.adjustmentForm.controls.quantity.updateValueAndValidity({ emitEvent: false });
  }

  private updateNotesValidators(): void {
    const reason = this.adjustmentForm.controls.reason.value;
    const validators =
      reason === 'OtherLoss' || reason === 'OtherGain'
        ? InputValidators.requiredText(500)
        : InputValidators.optionalText(500);
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

  private nullable(value: string | null | undefined): string | null {
    const normalized = value?.trim() ?? '';
    return normalized.length > 0 ? normalized : null;
  }

  private toIsoTimestamp(value: Date | null): string | null {
    return value ? formatUtcIsoInstant(value) : null;
  }

  private translate(key: string): string {
    return this.translocoService.translate(key);
  }
}
