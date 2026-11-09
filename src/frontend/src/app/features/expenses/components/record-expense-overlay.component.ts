import type { LocalizedError } from '../../../core/i18n/api-error-message.service';
import { signal as errorSignal } from '@angular/core';
import { TillPendingActionsComponent } from '../../tills/components/till-pending-actions.component';
import { ViewChild } from '@angular/core';
import { TillPostingFieldsComponent } from '../../tills/components/till-posting-fields.component';
import { Component, EventEmitter, OnInit, Output, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { filter, take } from 'rxjs/operators';

import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';

import { EXPENSE_TREATMENT_CHOICES, type ExpenseTreatment } from '../services/expense.service';
import { ExpenseCategoryDto } from '../services/expense-category.service';
import { ExpensesFacade } from '../state/expenses.facade';
import {
  CURRENCY_ADDON_PT,
  CURRENCY_INPUT_GROUP_PT,
  CURRENCY_INPUT_NUMBER_PT,
} from '../../../shared/primeng-pt.config';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';
import { InputValidators } from '../../../shared/forms/input-validation';

@Component({
  selector: 'app-record-expense-overlay',
  standalone: true,
  imports: [
    TillPendingActionsComponent,
    TillPostingFieldsComponent,
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

  private readonly intentId = crypto.randomUUID();
  readonly treatment = signal<ExpenseTreatment>('Unclassified');
  readonly treatmentChoices = EXPENSE_TREATMENT_CHOICES;
  selectTreatment(event: Event): void {
    const choice = (event.target as HTMLSelectElement).value;
    if (this.treatmentChoices.some((t) => t === choice))
      this.treatment.set(choice as ExpenseTreatment);
  }

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  readonly categories = toSignal(this.expensesFacade.categories$, {
    initialValue: [] as readonly ExpenseCategoryDto[],
  });
  readonly selectableCategories = computed(() =>
    this.categories().filter(
      (category) => category.name.trim().toLowerCase() !== 'supplier payments',
    ),
  );
  readonly isSubmitting = toSignal(this.expensesFacade.submitting$, {
    initialValue: false,
  });
  readonly localizedError =
    this.expensesFacade.localizedError ?? errorSignal<LocalizedError | undefined>(undefined);
  fieldError(field: string) {
    return this.localizedError()?.fieldErrors.find((error) => error.field === field);
  }

  controlInvalid(field: string): boolean {
    const control = this.form.get(field);
    return !!this.fieldError(field) || !!(control?.touched && control.invalid);
  }

  controlDescriptions(field: string, localId?: string): string | undefined {
    const control = this.form.get(field);
    return [localId && control?.touched && control.invalid ? localId : '', this.fieldError(field) ? 'record-expense-overlay-' + field + '-error' : ''].filter(Boolean).join(' ') || undefined;
  }

  controlInputPt(field: string, localId?: string, base: any = {}): any {
    return {...base, pcInputText: {...base.pcInputText, root: {...base.pcInputText?.root, 'aria-invalid': this.controlInvalid(field) ? 'true' : undefined, 'aria-describedby': this.controlDescriptions(field, localId)}}};
  }

  readonly serverError = toSignal(this.expensesFacade.error$, {
    initialValue: '',
  });

  @Output() readonly closeRequested = new EventEmitter<void>();

  @ViewChild(TillPostingFieldsComponent) tillFields!: TillPostingFieldsComponent;
  readonly tillError = signal('');
  readonly tillLocked = signal(false);
  onTillLocked(locked: boolean) {
    this.tillLocked.set(locked);
    if (locked) this.form.disable({ emitEvent: false });
    else this.form.enable({ emitEvent: false });
  }
  readonly form = this.formBuilder.nonNullable.group({
    categoryName: ['', InputValidators.requiredText(100)],
    amount: [0, InputValidators.positiveNumber()],
    paidTo: ['', InputValidators.requiredText(255)],
    description: ['', InputValidators.optionalText(500)],
    expenseDate: [new Date(), [Validators.required]],
  });

  constructor() {
    this.expensesFacade.mutationStatus$
      .pipe(
        takeUntilDestroyed(),
        filter((status) => status.type === 'record-expense' && status.succeeded === true),
        take(1),
      )
      .subscribe(() => {
        this.expensesFacade.clearMutationStatus();
        this.expensesFacade.refreshExpenses();
        this.closeRequested.emit();
      });
  }

  ngOnInit(): void {
    this.expensesFacade.clearError();
    this.expensesFacade.clearMutationStatus();
    this.expensesFacade.loadCategories();
  }

  onClose(): void {
    if (this.tillLocked() || this.isSubmitting()) {
      return;
    }
    this.closeRequested.emit();
  }

  onSubmit(): void {
    if (this.tillLocked() || this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.expensesFacade.clearError();
    this.expensesFacade.clearMutationStatus();

    const raw = this.form.getRawValue();
    let tillFields;
    try {
      tillFields = this.tillFields.fields(raw.amount);
    } catch {
      this.tillError.set('tills.evidenceRequired');
      return;
    }
    this.expensesFacade.recordExpense({
      ...tillFields,
      ...(this.tillFields.context.enabled() ? { operationKey: this.intentId } : {}),
      treatment: this.treatment(),
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
