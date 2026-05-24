import { CommonModule } from '@angular/common';
import { Component, Input, computed, inject, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { DialogModule } from 'primeng/dialog';

import { SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';
import { SupplierInfoCardComponent } from './supplier-detail/supplier-info-card.component';
import { SupplierLedgerTableComponent } from './supplier-detail/supplier-ledger-table.component';

@Component({
  selector: 'app-supplier-detail',
  standalone: true,
  imports: [CommonModule, DialogModule, SupplierInfoCardComponent, SupplierLedgerTableComponent, TranslocoPipe],
  templateUrl: './supplier-detail.component.html',
  styleUrl: './supplier-detail.component.scss',
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

  onVisibilityChange(visible: boolean): void {
    if (!visible) {
      this.facade.clearLedger();
      this.closeRequested.emit();
    }
  }
}
