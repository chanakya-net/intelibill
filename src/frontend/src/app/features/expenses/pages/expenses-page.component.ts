import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SkeletonModule } from 'primeng/skeleton';

import { AuthService } from '../../../core/auth/auth.service';
import { RecordExpenseOverlayComponent } from '../components/record-expense-overlay.component';
import { CorrectExpenseOverlayComponent } from '../components/correct-expense-overlay.component';
import { ExpensesFilterBarComponent, ExpenseStatusFilter } from '../components/expenses-filter-bar.component';
import { ExpensesTableComponent } from '../components/expenses-table.component';
import { ExpenseDto, ExpenseListItemDto } from '../services/expense.service';
import { ExpensesFacade } from '../state/expenses.facade';

interface ExpenseSummaryCard {
  readonly labelKey: string;
  readonly value: number;
  readonly variant: 'count' | 'money';
  readonly tone: 'amber' | 'sage' | 'terracotta' | 'ink';
}

interface ExpenseDirectoryMetric {
  readonly labelKey: string;
  readonly value: number;
  readonly variant: 'count' | 'money';
}

@Component({
  selector: 'app-expenses-page',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    ProgressSpinnerModule,
    SkeletonModule,
    TranslocoPipe,
    RecordExpenseOverlayComponent,
    CorrectExpenseOverlayComponent,
    ExpensesFilterBarComponent,
    ExpensesTableComponent,
  ],
  templateUrl: './expenses-page.component.html',
  styleUrl: './expenses-page.component.scss',
})
export class ExpensesPageComponent {
  private readonly authService = inject(AuthService);
  private readonly expensesFacade = inject(ExpensesFacade);

  readonly expenses$ = this.expensesFacade.expenses$;
  readonly loading$ = this.expensesFacade.loading$;
  readonly error$ = this.expensesFacade.error$;
  readonly pagination$ = this.expensesFacade.pagination$;

  readonly expenses = toSignal(this.expenses$, { initialValue: [] as ExpenseListItemDto[] });
  readonly loading = toSignal(this.loading$, { initialValue: false });
  readonly error = toSignal(this.error$, { initialValue: '' });
  readonly pagination = toSignal(this.pagination$, { initialValue: { totalCount: 0, currentPage: 1, pageSize: 20 } });
  readonly selectedExpense = toSignal(this.expensesFacade.selectedExpense$, { initialValue: null as ExpenseDto | null });

  readonly showRecordOverlay = signal(false);
  readonly showCorrectOverlay = signal(false);
  readonly selectedExpenseId = signal<string | null>(null);
  readonly searchValue = signal('');
  readonly statusFilter = signal<ExpenseStatusFilter>('all');

  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) {
      return '';
    }

    const activeShop = session.shops.find((shop) => shop.shopId === session.activeShopId) ?? session.shops.find((shop) => shop.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canManageExpenses = computed(() => ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()));
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.pagination().totalCount / this.pagination().pageSize)));

  readonly filteredExpenses = computed(() => {
    const statusFilter = this.statusFilter();

    if (statusFilter === 'all') {
      return [...this.expenses()];
    }

    return this.expenses().filter((expense) => expense.isVoided === (statusFilter === 'voided'));
  });

  readonly totalExpenses = computed(() => this.pagination().totalCount);
  readonly pageAmount = computed(() =>
    this.sumBy(this.expenses().filter((expense) => !expense.isVoided), (expense) => expense.amount),
  );
  readonly activeCount = computed(() => this.expenses().filter((expense) => !expense.isVoided).length);
  readonly voidedCount = computed(() => this.expenses().filter((expense) => expense.isVoided).length);
  readonly filteredRowCount = computed(() => this.filteredExpenses().length);
  readonly showPagination = computed(() => this.pagination().totalCount > this.pagination().pageSize);

  readonly summaryCards = computed<ExpenseSummaryCard[]>(() => [
    {
      labelKey: 'expenses.summary.totalExpenses',
      value: this.totalExpenses(),
      variant: 'count',
      tone: 'amber',
    },
    {
      labelKey: 'expenses.summary.pageAmount',
      value: this.pageAmount(),
      variant: 'money',
      tone: 'terracotta',
    },
    {
      labelKey: 'expenses.summary.activeCount',
      value: this.activeCount(),
      variant: 'count',
      tone: 'sage',
    },
    {
      labelKey: 'expenses.summary.voidedCount',
      value: this.voidedCount(),
      variant: 'count',
      tone: 'ink',
    },
  ]);

  readonly directoryMetrics = computed<ExpenseDirectoryMetric[]>(() => [
    {
      labelKey: 'expenses.summary.pageAmount',
      value: this.pageAmount(),
      variant: 'money',
    },
    {
      labelKey: 'expenses.summary.activeCount',
      value: this.activeCount(),
      variant: 'count',
    },
    {
      labelKey: 'expenses.summary.filteredRows',
      value: this.filteredRowCount(),
      variant: 'count',
    },
  ]);

  constructor() {
    this.expensesFacade.loadExpenses();
  }

  onSearchValueChange(value: string): void {
    this.searchValue.set(value);
    this.expensesFacade.loadExpenses(value || undefined, 1);
  }

  onStatusFilterChange(statusFilter: ExpenseStatusFilter): void {
    this.statusFilter.set(statusFilter);
  }

  onPageChange(page: number): void {
    this.expensesFacade.loadExpenses(this.searchValue() || undefined, page);
  }

  openRecordOverlay(): void {
    this.expensesFacade.clearError();
    this.expensesFacade.clearMutationStatus();
    this.showRecordOverlay.set(true);
  }

  closeRecordOverlay(): void {
    this.showRecordOverlay.set(false);
  }

  openCorrectOverlay(expenseId: string): void {
    this.expensesFacade.clearError();
    this.expensesFacade.clearMutationStatus();
    this.selectedExpenseId.set(expenseId);
    this.expensesFacade.loadExpenseDetail(expenseId);
    this.showCorrectOverlay.set(true);
  }

  closeCorrectOverlay(): void {
    this.showCorrectOverlay.set(false);
    this.selectedExpenseId.set(null);
    this.expensesFacade.clearExpenseDetail();
  }

  private sumBy<T>(items: readonly T[], selector: (item: T) => number): number {
    return items.reduce((total, item) => total + selector(item), 0);
  }
}
