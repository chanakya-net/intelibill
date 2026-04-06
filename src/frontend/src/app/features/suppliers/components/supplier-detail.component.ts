import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, inject, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { BadgeModule } from 'primeng/badge';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { Supplier } from '../services/supplier.service';
import { SupplierLedgerEntry, SupplierLedgerService } from '../services/supplier-ledger.service';

@Component({
  selector: 'app-supplier-detail',
  standalone: true,
  imports: [BadgeModule, CommonModule, DialogModule, InputTextModule, ProgressSpinnerModule, TableModule, TranslocoPipe],
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
            [sortField]="'entryDate'"
            [sortOrder]="-1"
            [tableStyle]="{ 'min-width': '56rem' }"
            class="p-datatable-sm"
          >
          <ng-template pTemplate="header">
            <tr>
              <th pSortableColumn="entryTypeLabel">{{ 'suppliers.entryType' | transloco }} <p-sortIcon field="entryTypeLabel" /></th>
              <th pSortableColumn="amount">{{ 'suppliers.amount' | transloco }} <p-sortIcon field="amount" /></th>
              <th pSortableColumn="entryDate">{{ 'suppliers.entryDate' | transloco }} <p-sortIcon field="entryDate" /></th>
              <th pSortableColumn="notes">{{ 'suppliers.notes' | transloco }} <p-sortIcon field="notes" /></th>
            </tr>
            <tr>
              <th>
                <input
                  pInputText
                  type="text"
                  [placeholder]="'suppliers.searchEntryType' | transloco"
                  (input)="ledgerTable.filter($any($event.target).value, 'entryTypeLabel', 'contains')"
                />
              </th>
              <th>
                <input
                  pInputText
                  type="text"
                  [placeholder]="'suppliers.searchAmount' | transloco"
                  (input)="ledgerTable.filter($any($event.target).value, 'amount', 'contains')"
                />
              </th>
              <th>
                <input
                  pInputText
                  type="text"
                  [placeholder]="'suppliers.searchEntryDate' | transloco"
                  (input)="ledgerTable.filter($any($event.target).value, 'entryDate', 'contains')"
                />
              </th>
              <th>
                <input
                  pInputText
                  type="text"
                  [placeholder]="'suppliers.searchNotes' | transloco"
                  (input)="ledgerTable.filter($any($event.target).value, 'notes', 'contains')"
                />
              </th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-entry>
            <tr>
              <td>
                <span class="entry-type" [ngClass]="entry.entryTypeClass">
                  {{ entry.entryTypeLabel }}
                </span>
              </td>
              <td>
                <p-badge [value]="formatSignedAmount(entry.amount)" [severity]="amountSeverity(entry.amount)" />
              </td>
              <td>{{ entry.entryDate }}</td>
              <td>{{ entry.notes || '-' }}</td>
            </tr>
          </ng-template>

          <ng-template pTemplate="footer" *ngIf="entries().length > 0">
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

      .search-label {
        font-weight: 600;
        color: #1f2937;
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
        font-weight: 600;
      }

      .payment-made {
        color: var(--blue-600);
        font-weight: 600;
      }

      .record-adjusted {
        color: var(--orange-600);
        font-weight: 600;
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

      input[pinputtext] {
        width: 100%;
      }
    }
  `,
})
export class SupplierDetailComponent {
  private readonly ledgerService = inject(SupplierLedgerService);
  private readonly transloco = inject(TranslocoService);

  @Input() set supplier(value: Supplier | null) {
    this.currentSupplier.set(value);
  }

  @Input() set supplierId(value: string | null) {
    if (value) {
      this.loadLedgerEntries(value);
    }
  }

  readonly closeRequested = output<void>();
  readonly currentSupplier = signal<Supplier | null>(null);

  readonly isOpen = true;
  protected readonly isLoading = () => this._isLoading;
  protected readonly entries = () => this._entries;
  protected readonly tableEntries = () => this._tableEntries;
  protected readonly totalAmount = () => this._totalAmount;

  private _isLoading = false;
  private _entries: SupplierLedgerEntry[] = [];
  private _tableEntries: Array<SupplierLedgerEntry & { entryTypeLabel: string; entryTypeClass: string }> = [];
  private _totalAmount = 0;

  private loadLedgerEntries(supplierId: string): void {
    this._isLoading = true;
    this.ledgerService.getSupplierLedgerEntries(supplierId).subscribe(
      (entries) => {
        this._entries = [...entries];
        this._tableEntries = entries.map((entry) => ({
          ...entry,
          entryTypeLabel: this.getEntryTypeLabel(entry.entryType),
          entryTypeClass: this.getEntryTypeClass(entry.entryType),
        }));
        this._totalAmount = this._entries.reduce((sum, entry) => sum + entry.amount, 0);
        this._isLoading = false;
      },
      () => {
        this._isLoading = false;
        this._entries = [];
        this._tableEntries = [];
        this._totalAmount = 0;
      }
    );
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
    if (amount > 0) {
      return 'danger';
    }

    if (amount < 0) {
      return 'success';
    }

    return 'secondary';
  }

  onVisibilityChange(visible: boolean): void {
    if (!visible) {
      this.closeRequested.emit();
    }
  }
}

