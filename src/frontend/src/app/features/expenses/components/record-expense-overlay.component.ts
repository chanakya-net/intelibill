import { Component, EventEmitter, OnInit, Output, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { filter } from 'rxjs/operators';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ExpenseCategoryDto } from '../services/expense-category.service';
import { ExpensesFacade } from '../state/expenses.facade';

@Component({
  selector: 'app-record-expense-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
  ],
  templateUrl: './record-expense-overlay.component.html',
})
export class RecordExpenseOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly expensesFacade = inject(ExpensesFacade);

  readonly categories = toSignal(this.expensesFacade.categories$, {
    initialValue: [] as readonly ExpenseCategoryDto[],
  });
  readonly isSubmitting = toSignal(this.expensesFacade.submitting$, {
    initialValue: false,
  });
  readonly serverError = toSignal(this.expensesFacade.error$, {
    initialValue: '',
  });

  @Output() readonly close = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    categoryName: ['', [Validators.required, Validators.maxLength(100)]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    paidTo: ['', [Validators.required, Validators.maxLength(255)]],
    description: ['', [Validators.maxLength(500)]],
    expenseDate: [new Date().toISOString().split('T')[0], [Validators.required]],
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
        this.close.emit();
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
    this.close.emit();
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
      expenseDate: raw.expenseDate,
    });
  }

  private nullableTrimmed(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
