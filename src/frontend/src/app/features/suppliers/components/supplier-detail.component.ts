import { CommonModule } from '@angular/common';
import { Component, Input, computed, inject, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { Table, TableModule } from 'primeng/table';

import { SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';

@Component({
  selector: 'app-supplier-detail',
  standalone: true,
  imports: [
    BadgeModule,
    ButtonModule,
    CommonModule,
    DialogModule,
    FormsModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    ProgressSpinnerModule,
    TableModule,
    TranslocoPipe,
  ],
  template: `
    <p-dialog
      [(visible)]="isOpen"
      (visibleChange)="onVisibilityChange($event)"
      [header]="'suppliers.ledgerEntries' | transloco"
      [modal]="true"
      [style]="{ width: '100%', maxWidth: '1120px' }"
      [draggable]="false"
    >
      <div class="loading" *ngIf="isLoading()">
        <p-progressSpinner styleClass="h-8 w-8" strokeWidth="6"></p-progressSpinner>
      </div>

      <div class="ledger-content" *ngIf="!isLoading()">
        <div class="supplier-info-header" *ngIf="currentSupplier()">
          <div class="info-row">
            <span class="label">{{ 'suppliers.name' | transloco }}:</span>
            <span class="value">{{ currentSupplier()!.name }}</span>
          </div>
          <div class="info-row">
            <span class="label">{{ 'suppliers.contactPerson' | transloco }}:</span>
            <span class="value">{{ currentSupplier()!.contactPersonName || '-' }}</span>
          </div>
          <div class="info-row">
            <span class="label">{{ 'suppliers.contactPhone' | transloco }}:</span>
            <span class="value">{{ currentSupplier()!.contactPersonPhone || '-' }}</span>
          </div>
        </div>

        <h3 class="details-heading">{{ 'suppliers.details' | transloco }}</h3>

        <div class="ledger-table-card">
          <p-table
            #ledgerTable
            [value]="tableEntries()"
            dataKey="id"
            [rows]="10"
            [rowsPerPageOptions]="[10, 25, 50]"
            [paginator]="tableEntries().length > 10"
            [globalFilterFields]="['entryTypeLabel', 'amount', 'entryDate', 'notes']"
            [sortField]="'entryDate'"
            [sortOrder]="-1"
            [tableStyle]="{ 'min-width': '56rem' }"
            showGridlines
            class="p-datatable-sm"
          >
            <ng-template pTemplate="caption">
              <div class="table-caption">
                <p-button
                  [label]="'common.clear' | transloco"
                  [outlined]="true"
                  icon="pi pi-filter-slash"
                  (click)="clearFilters(ledgerTable)"
                />
                <p-iconfield iconPosition="left">
                  <p-inputicon>
                    <i class="pi pi-search"></i>
                  </p-inputicon>
                  <input
                    pInputText
                    type="text"
                    [(ngModel)]="searchValue"
                    (input)="ledgerTable.filterGlobal(searchValue(), 'contains')"
                    [placeholder]="'suppliers.searchLedger' | transloco"
                  />
                </p-iconfield>
              </div>
            </ng-template>

            <ng-template pTemplate="header">
              <tr>
                <th pSortableColumn="entryTypeLabel" style="min-width: 12rem">
                  <div class="flex items-center justify-between">
                    {{ 'suppliers.entryType' | transloco }}
                    <div class="flex items-center gap-1">
                      <p-sortIcon field="entryTypeLabel" />
                      <p-columnFilter type="text" field="entryTypeLabel" display="menu" />
                    </div>
                  </div>
                </th>
                <th pSortableColumn="amount" style="min-width: 10rem">
                  <div class="flex items-center justify-between">
                    {{ 'suppliers.amount' | transloco }}
                    <div class="flex items-center gap-1">
                      <p-sortIcon field="amount" />
                      <p-columnFilter type="numeric" field="amount" display="menu" currency="INR" />
                    </div>
                  </div>
                </th>
                <th pSortableColumn="entryDate" style="min-width: 10rem">
                  <div class="flex items-center justify-between">
                    {{ 'suppliers.entryDate' | transloco }}
                    <div class="flex items-center gap-1">
                      <p-sortIcon field="entryDate" />
                      <p-columnFilter type="text" field="entryDate" display="menu" />
                    </div>
                  </div>
                </th>
                <th pSortableColumn="notes" style="min-width: 12rem">
                  <div class="flex items-center justify-between">
                    {{ 'suppliers.notes' | transloco }}
                    <div class="flex items-center gap-1">
                      <p-sortIcon field="notes" />
                      <p-columnFilter type="text" field="notes" display="menu" />
                    </div>
                  </div>
                </th>
              </tr>
            </ng-template>

            <ng-template pTemplate="body" let-entry>
              <tr [ngClass]="{ 'payment-made-row': entry.isPayment }">
                <td>
                  <span class="entry-type" [ngClass]="entry.entryTypeClass">
                    {{ entry.entryTypeLabel }}
                  </span>
                </td>
                <td>
                  <p-badge [value]="formatSignedAmount(entry.displayAmount)" [severity]="amountSeverity(entry.displayAmount)" />
                </td>
                <td>{{ entry.entryDate }}</td>
                <td>{{ entry.notes || '-' }}</td>
              </tr>
            </ng-template>

            <ng-template pTemplate="footer" *ngIf="tableEntries().length > 0">
              <tr class="total-row">
                <td colspan="3">{{ 'suppliers.totalAmount' | transloco }}</td>
                <td>{{ totalAmount() | currency: 'INR':'symbol':'1.0-2' }}</td>
              </tr>
            </ng-template>

            <ng-template pTemplate="emptymessage">
              <tr>
                <td [attr.colspan]="4">
                  <div class="empty-state">
                    <p>{{ 'suppliers.noEntriesFound' | transloco }}</p>
                  </div>
                </td>
              </tr>
            </ng-template>
          </p-table>
        </div>
      </div>
    </p-dialog>
  `,
  styles: `
    :host {
      ::ng-deep .loading {
        display: flex;
        justify-content: center;
        align-items: center;
        padding: 2rem;
      }

      .ledger-content {
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
      }

      .supplier-info-header {
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 0.5rem;
        padding: 0.6rem 0.75rem;
        border: 1px solid #fdba74;
        border-radius: 0.75rem;
        background: linear-gradient(160deg, #ffffff, #fff7ed);
      }

      .ledger-table-card {
        border: 1px solid #fdba74;
        border-radius: 1rem;
        background: linear-gradient(160deg, #ffffff, #fff7ed);
        overflow: hidden;
      }

      .details-heading {
        margin: 0.25rem 0 0.35rem;
        font-size: 1.05rem;
        font-weight: 700;
        color: #1f2937;
        letter-spacing: 0.01em;
      }

      .table-caption {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.75rem;
        padding: 0.75rem;
      }

      .info-row {
        display: flex;
        flex-direction: row;
        align-items: baseline;
        gap: 0.35rem;
        white-space: nowrap;

        .label {
          font-weight: 600;
          font-size: 0.9rem;
          color: var(--text-color-secondary);
        }

        .value {
          color: var(--text-color);
          font-size: 0.95rem;
          font-weight: 500;
          overflow: hidden;
          text-overflow: ellipsis;
        }
      }

      .empty-state {
        text-align: center;
        padding: 2rem;
        color: var(--text-color-secondary);
      }

      .goods-received {
        color: var(--green-600);
      }

      .payment-made {
        color: var(--green-700);
      }

      ::ng-deep .payment-made-row td {
        background-color: #dcfce7 !important;
      }

      .record-adjusted {
        color: var(--orange-600);
      }

      .total-row td {
        font-weight: 700;
      }

      .entry-type {
        white-space: nowrap;
      }

      @media (max-width: 900px) {
        .supplier-info-header {
          grid-template-columns: 1fr;
        }

        .info-row {
          white-space: normal;
        }
      }
    }
  `,
})
export class SupplierDetailComponent {
  private readonly facade = inject(SuppliersFacade);
  private readonly transloco = inject(TranslocoService);
  private readonly currentLang = toSignal(this.transloco.langChanges$, { initialValue: '' });

  @Input() set supplier(value: Supplier | null) {
    this.currentSupplier.set(value);
  }

  @Input() set supplierId(value: string | null) {
    if (value) {
      this.facade.loadLedger(value);
    }
  }

  readonly closeRequested = output<void>();
  readonly currentSupplier = signal<Supplier | null>(null);
  readonly isOpen = true;
  protected readonly searchValue = signal('');

  protected readonly isLoading = this.facade.ledgerIsLoading;

  protected readonly tableEntries = computed(() => {
    this.currentLang(); // re-run on language change
    return this.facade.ledgerEntries().map((entry: SupplierLedgerEntry) => {
      const isPayment = entry.entryType === 2 || entry.entryType === 'PAYMENT_MADE';
      return {
        ...entry,
        entryTypeLabel: this.getEntryTypeLabel(entry.entryType),
        entryTypeClass: this.getEntryTypeClass(entry.entryType),
        isPayment,
        displayAmount: isPayment ? -Math.abs(entry.amount) : entry.amount,
      };
    });
  });

  protected readonly totalAmount = computed(() =>
    this.facade.ledgerEntries().reduce((sum: number, e: SupplierLedgerEntry) => {
      const isPayment = e.entryType === 2 || e.entryType === 'PAYMENT_MADE';
      return sum + (isPayment ? -Math.abs(e.amount) : e.amount);
    }, 0)
  );

  clearFilters(table: Table): void {
    table.clear();
    this.searchValue.set('');
  }

  getEntryTypeLabel(entryType: SupplierLedgerEntry['entryType']): string {
    switch (entryType) {
      case 1:
      case 'GOODS_RECEIVED':
        return this.transloco.translate('suppliers.entryTypes.goodsReceived');
      case 2:
      case 'PAYMENT_MADE':
        return this.transloco.translate('suppliers.entryTypes.paymentMade');
      case 3:
      case 'RECORD_ADJUSTED':
        return this.transloco.translate('suppliers.entryTypes.recordAdjusted');
      default:
        return String(entryType);
    }
  }

  getEntryTypeClass(entryType: SupplierLedgerEntry['entryType']): string {
    switch (entryType) {
      case 1:
      case 'GOODS_RECEIVED':
        return 'goods-received';
      case 2:
      case 'PAYMENT_MADE':
        return 'payment-made';
      case 3:
      case 'RECORD_ADJUSTED':
        return 'record-adjusted';
      default:
        return '';
    }
  }

  formatSignedAmount(amount: number): string {
    const formatted = new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      minimumFractionDigits: 0,
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

  onVisibilityChange(visible: boolean): void {
    if (!visible) {
      this.facade.clearLedger();
      this.closeRequested.emit();
    }
  }
}
