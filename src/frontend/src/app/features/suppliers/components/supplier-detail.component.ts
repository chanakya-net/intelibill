import { CommonModule } from '@angular/common';
import { Component, Input, computed, effect, inject, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';

import { MakePaymentRequest, SupplierLedgerEntry } from '../services/supplier-ledger.service';
import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';
import { SupplierInfoCardComponent } from './supplier-detail/supplier-info-card.component';
import { SupplierLedgerTableComponent } from './supplier-detail/supplier-ledger-table.component';
import { SupplierPaymentFormComponent } from './supplier-detail/supplier-payment-form.component';

@Component({
  selector: 'app-supplier-detail',
  standalone: true,
  imports: [
    CommonModule,
    DialogModule,
    ButtonModule,
    SupplierInfoCardComponent,
    SupplierLedgerTableComponent,
    SupplierPaymentFormComponent,
    TranslocoPipe,
  ],
  templateUrl: './supplier-detail.component.html',
  styleUrl: './supplier-detail.component.scss',
})
export class SupplierDetailComponent {
  readonly facade = inject(SuppliersFacade);
  private readonly transloco = inject(TranslocoService);
  private readonly currentLang = toSignal(this.transloco.langChanges$, { initialValue: '' });

  @Input() canMakePayment = false;

  @Input() set supplier(value: Supplier | null) {
    this.currentSupplier.set(value);
  }

  @Input() set supplierId(value: string | null) {
    this.currentSupplierId.set(value);
    if (value) {
      this.isOpen.set(true);
      this.facade.loadLedger(value);
    }
  }

  readonly closeRequested = output<void>();
  readonly currentSupplier = signal<Supplier | null>(null);
  readonly currentSupplierId = signal<string | null>(null);
  readonly isOpen = signal(false);
  readonly ledgerFilter = signal<'all' | 'goods' | 'payments'>('all');

  protected readonly isSubmittingPayment = this.facade.isSubmitting;
  protected readonly paymentErrorMessage = this.facade.errorMessage;

  constructor() {
    effect(() => {
      if (!this.facade.lastMutationSucceeded() || this.facade.lastMutationType() !== 'make-payment') {
        return;
      }
      const supplierId = this.currentSupplierId();
      if (!supplierId) {
        return;
      }
      this.facade.loadLedger(supplierId);
      this.facade.clearMutationStatus();
    });
  }

  onPaymentSubmitted(payload: MakePaymentRequest): void {
    const supplierId = this.currentSupplierId();
    if (!supplierId) {
      return;
    }
    this.facade.clearError();
    this.facade.clearMutationStatus();
    this.facade.makePayment(supplierId, payload);
  }

  protected readonly isLoading = this.facade.ledgerIsLoading;

  protected readonly tableEntries = computed(() => {
    this.currentLang(); // re-run on language change
    const currentBalanceDue = this.currentSupplier()?.balanceDue ?? 0;
    let rows = this.facade.ledgerEntries().map((entry: SupplierLedgerEntry) => {
      const isPayment = entry.entryType === 2 || entry.entryType === 'PAYMENT_MADE';
      return {
        ...entry,
        entryTypeLabel: this.getEntryTypeLabel(entry.entryType),
        entryTypeClass: this.getEntryTypeClass(entry.entryType),
        isPayment,
        displayAmount: isPayment ? -Math.abs(entry.amount) : entry.amount,
      };
    });

    const filter = this.ledgerFilter();
    if (filter === 'goods') {
      rows = rows.filter((r) => !r.isPayment);
    } else if (filter === 'payments') {
      rows = rows.filter((r) => r.isPayment);
    }

    const sorted = [...rows].sort((a, b) => (b.entryDate ?? '') > (a.entryDate ?? '') ? 1 : -1);

    let runningBalance = currentBalanceDue;
    return sorted.map((row) => {
      const balance = runningBalance;
      runningBalance = runningBalance - row.displayAmount;
      return { ...row, balance };
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
    this.isOpen.set(visible);
    if (!visible) {
      this.currentSupplierId.set(null);
      this.facade.clearLedger();
      this.closeRequested.emit();
    }
  }
}
