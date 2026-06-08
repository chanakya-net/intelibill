import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';

import { ExpenseListItemDto } from '../services/expense.service';
import { DateOnlyPipe } from '../../../shared/pipes/date-only.pipe';

@Component({
  selector: 'app-expenses-table',
  standalone: true,
  imports: [CommonModule, ButtonModule, TableModule, DateOnlyPipe, TranslocoPipe],
  templateUrl: './expenses-table.component.html',
  styleUrl: './expenses-table.component.scss',
})
export class ExpensesTableComponent {
  @Input({ required: true }) expenses: readonly ExpenseListItemDto[] = [];
  @Input() visibleRows = 0;
  @Input() totalRows = 0;
  @Input() canManageExpenses = false;
  @Input() currentPage = 1;
  @Input() totalPages = 1;
  @Input() showPagination = false;

  @Output() correctExpense = new EventEmitter<string>();
  @Output() pageChange = new EventEmitter<number>();

  get tableExpenses(): ExpenseListItemDto[] {
    return [...this.expenses];
  }

  onCorrectExpense(expenseId: string): void {
    this.correctExpense.emit(expenseId);
  }

  onPageChange(page: number): void {
    this.pageChange.emit(page);
  }

  categoryInitials(categoryName: string): string {
    const words = categoryName.trim().split(/\s+/);
    if (words.length === 1) {
      return words[0].substring(0, 2).toUpperCase();
    }

    return (words[0][0] + words[1][0]).toUpperCase();
  }

  categoryAvatarColor(categoryName: string): string {
    const colors = ['#b45309', '#0369a1', '#15803d', '#7c3aed', '#be185d', '#c2410c', '#0f766e', '#1d4ed8'];
    let hash = 0;
    for (let index = 0; index < categoryName.length; index++) {
      hash = categoryName.charCodeAt(index) + ((hash << 5) - hash);
    }

    return colors[Math.abs(hash) % colors.length];
  }
}
