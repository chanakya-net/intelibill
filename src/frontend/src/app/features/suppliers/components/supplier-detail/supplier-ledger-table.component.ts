import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, Input, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { Table, TableModule } from 'primeng/table';

type LedgerRow = {
  readonly id: string;
  readonly entryTypeLabel: string;
  readonly entryTypeClass: string;
  readonly isPayment: boolean;
  readonly displayAmount: number;
  readonly balance: number;
  readonly entryDate: string;
  readonly notes: string | null;
};

@Component({
  selector: 'app-supplier-ledger-table',
  standalone: true,
  imports: [
    BadgeModule,
    ButtonModule,
    CommonModule,
    CurrencyPipe,
    FormsModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    ProgressSpinnerModule,
    TableModule,
    TranslocoPipe,
  ],
  templateUrl: './supplier-ledger-table.component.html',
  styleUrl: './supplier-ledger-table.component.scss',
})
export class SupplierLedgerTableComponent {
  @Input({ required: true }) entries: LedgerRow[] = [];
  @Input({ required: true }) loading = false;

  protected readonly searchValue = signal('');

  protected readonly filteredEntries = computed(() => {
    const q = this.searchValue().toLowerCase();
    const entries = this.entries ?? [];
    const filtered = q
      ? entries.filter(
          (e) =>
            e.entryTypeLabel.toLowerCase().includes(q) ||
            (e.notes ?? '').toLowerCase().includes(q) ||
            (e.entryDate ?? '').includes(q),
        )
      : entries;
    return [...filtered].sort((a, b) =>
      (b.entryDate ?? '') > (a.entryDate ?? '') ? 1 : -1,
    );
  });

  protected readonly totalAmount = computed(() =>
    (this.entries ?? []).reduce((sum, e) => sum + (e.displayAmount ?? 0), 0),
  );

  clearFilters(table: Table): void {
    table.clear();
    this.searchValue.set('');
  }

  formatSignedAmount(amount: number): string {
    const formatted = new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(Math.abs(amount));

    if (amount > 0) {
      return `+${formatted}`;
    }

    if (amount < 0) {
      return `-${formatted}`;
    }

    return formatted;
  }

  amountSeverity(amount: number): 'danger' | 'success' | 'secondary' {
    if (amount > 0) return 'danger';
    if (amount < 0) return 'success';
    return 'secondary';
  }
}
