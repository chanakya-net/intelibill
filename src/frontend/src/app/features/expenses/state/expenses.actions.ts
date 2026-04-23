import { createActionGroup, emptyProps, props } from '@ngrx/store';

import { ExpenseDto, ExpenseListItemDto, RecordExpenseRequest } from '../services/expense.service';
import { ExpenseCategoryDto } from '../services/expense-category.service';

export type ExpenseMutationType = 'record-expense' | 'correct-expense';

export const ExpensesActions = createActionGroup({
  source: 'Expenses',
  events: {
    'Load Expenses Requested': props<{ search?: string; page?: number; pageSize?: number }>(),
    'Load Expenses Succeeded': props<{ expenses: readonly ExpenseListItemDto[]; totalCount: number; page: number; pageSize: number }>(),
    'Load Expenses Failed': props<{ errorMessage: string }>(),

    'Load Expense Detail Requested': props<{ expenseId: string }>(),
    'Load Expense Detail Succeeded': props<{ expense: ExpenseDto }>(),
    'Load Expense Detail Failed': props<{ errorMessage: string }>(),

    'Load Categories Requested': emptyProps(),
    'Load Categories Succeeded': props<{ categories: readonly ExpenseCategoryDto[] }>(),
    'Load Categories Failed': props<{ errorMessage: string }>(),

    'Record Expense Requested': props<{ payload: RecordExpenseRequest }>(),
    'Record Expense Succeeded': props<{ expense: ExpenseDto }>(),
    'Record Expense Failed': props<{ errorMessage: string }>(),

    'Correct Expense Requested': props<{ expenseId: string; payload: RecordExpenseRequest }>(),
    'Correct Expense Succeeded': props<{ expense: ExpenseDto }>(),
    'Correct Expense Failed': props<{ errorMessage: string }>(),

    'Clear Error': emptyProps(),
    'Clear Mutation Status': emptyProps(),
    'Clear Expense Detail': emptyProps(),
  },
});
