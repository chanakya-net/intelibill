import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import { AuthService } from '../../../core/auth/auth.service';
import { RecordExpenseOverlayComponent } from '../components/record-expense-overlay.component';
import { CorrectExpenseOverlayComponent } from '../components/correct-expense-overlay.component';
import { ExpenseDto, ExpenseListItemDto } from '../services/expense.service';
import { ExpensesFacade } from '../state/expenses.facade';

@Component({
  selector: 'app-expenses-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CardModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    ProgressSpinnerModule,
    TableModule,
    TagModule,
    TranslocoPipe,
    RecordExpenseOverlayComponent,
    CorrectExpenseOverlayComponent,
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
  readonly totalPages = computed(() => Math.ceil(this.pagination().totalCount / this.pagination().pageSize));

  constructor() {
    this.expensesFacade.loadExpenses();
  }

  onSearch(): void {
    this.expensesFacade.loadExpenses(this.searchValue() || undefined, 1);
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
}
