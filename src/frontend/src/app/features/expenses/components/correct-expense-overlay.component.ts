import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { filter } from 'rxjs/operators';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { ExpenseCategoryDto } from '../services/expense-category.service';
import { ExpenseDto } from '../services/expense.service';
import { ExpensesFacade } from '../state/expenses.facade';

@Component({
  selector: 'app-correct-expense-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
  ],
  templateUrl: './correct-expense-overlay.component.html',
})
export class CorrectExpenseOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly expensesFacade = inject(ExpensesFacade);

  @Input({ required: true }) expenseId!: string;
  @Input({ required: true }) originalExpense!: ExpenseDto;

  @Output() readonly close = new EventEmitter<void>();

  readonly categories = toSignal(this.expensesFacade.categories$, {
    initialValue: [] as readonly ExpenseCategoryDto[],
  });
  readonly isSubmitting = toSignal(this.expensesFacade.submitting$, {
    initialValue: false,
  });
  readonly serverError = toSignal(this.expensesFacade.error$, {
    initialValue: '',
  });

  readonly form = this.formBuilder.nonNullable.group({
    categoryName: ['', [Validators.required, Validators.maxLength(100)]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    paidTo: ['', [Validators.required, Validators.maxLength(255)]],
    description: ['', [Validators.maxLength(500)]],
    expenseDate: ['', [Validators.required]],
  });

  constructor() {
    this.expensesFacade.mutationStatus$
      .pipe(
        takeUntilDestroyed(),
        filter(
          (status) =>
            status.type === 'correct-expense' && status.succeeded === true
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

    this.form.patchValue({
      categoryName: this.originalExpense.categoryName,
      amount: this.originalExpense.amount,
      paidTo: this.originalExpense.paidTo,
      description: this.originalExpense.description ?? '',
      expenseDate: this.originalExpense.expenseDate.split('T')[0],
    });
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
    this.expensesFacade.correctExpense(this.expenseId, {
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
