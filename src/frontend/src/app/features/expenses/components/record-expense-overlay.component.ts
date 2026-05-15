import { Component, EventEmitter, OnInit, Output, computed, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { filter } from 'rxjs/operators';

import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';

import { ExpenseCategoryDto } from '../services/expense-category.service';
import { ExpensesFacade } from '../state/expenses.facade';
import { CURRENCY_ADDON_PT, CURRENCY_INPUT_GROUP_PT, CURRENCY_INPUT_NUMBER_PT } from '../../../shared/primeng-pt.config';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

@Component({
  selector: 'app-record-expense-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    SelectModule,
    InputNumberModule,
    DatePickerModule,
    TextareaModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
    InputGroupModule,
    InputGroupAddonModule,
  ],
  templateUrl: './record-expense-overlay.component.html',
  styleUrls: ['./record-expense-overlay.component.scss'],
})
export class RecordExpenseOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly expensesFacade = inject(ExpensesFacade);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  readonly categories = toSignal(this.expensesFacade.categories$, {
    initialValue: [] as readonly ExpenseCategoryDto[],
  });
  readonly selectableCategories = computed(() =>
    this.categories().filter(
      (category) => category.name.trim().toLowerCase() !== 'supplier payments'
    )
  );
  readonly isSubmitting = toSignal(this.expensesFacade.submitting$, {
    initialValue: false,
  });
  readonly serverError = toSignal(this.expensesFacade.error$, {
    initialValue: '',
  });

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    categoryName: ['', [Validators.required, Validators.maxLength(100)]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    paidTo: ['', [Validators.required, Validators.maxLength(255)]],
    description: ['', [Validators.maxLength(500)]],
    expenseDate: [new Date(), [Validators.required]],
  });

  constructor() {
    this.expensesFacade.mutationStatus$
      .pipe(
        takeUntilDestroyed(),
        filter(
          (status) =>
            status.type === 'record-expense' && status.succeeded === true
        )
      )
      .subscribe(() => {
        this.expensesFacade.loadExpenses();
        this.closeRequested.emit();
        this.expensesFacade.clearMutationStatus();
      });
  }

  ngOnInit(): void {
    this.expensesFacade.clearError();
    this.expensesFacade.clearMutationStatus();
    this.expensesFacade.loadCategories();
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.closeRequested.emit();
  }

  onSubmit(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.expensesFacade.clearError();
    this.expensesFacade.clearMutationStatus();

    const raw = this.form.getRawValue();
    this.expensesFacade.recordExpense({
      categoryName: raw.categoryName.trim(),
      amount: raw.amount,
      paidTo: raw.paidTo.trim(),
      description: this.nullableTrimmed(raw.description),
      expenseDate: this.toIsoDate(raw.expenseDate),
    });
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }

  private toIsoDate(value: Date | string): string {
    if (typeof value === 'string') {
      return value;
    }

    return formatLocalIsoDate(value);
  }
}
