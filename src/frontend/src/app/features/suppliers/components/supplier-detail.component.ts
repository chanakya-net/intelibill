import { CommonModule } from '@angular/common';
import { Component, Input, computed, effect, inject, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';

import { AuthService } from '../../../core/auth/auth.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
import { PendingFinancialOperationComponent } from '../../../shared/components/pending-financial-operation/pending-financial-operation.component';
import {
  MakePaymentRequest,
  SupplierLedgerEntry,
  supplierLedgerEntryKind,
} from '../services/supplier-ledger.service';
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
    PendingFinancialOperationComponent,
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
  private readonly intents = inject(FinancialOperationIntentService);
  private readonly authService = inject(AuthService);

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
  protected readonly pendingNotice = signal<string | null>(null);
  protected readonly isCheckingPayment = signal(false);
  private readonly scope = computed(() => ({
    userId: this.authService.session()?.user?.id ?? '',
    shopId: this.authService.session()?.activeShopId ?? '',
  }));
  /** An earlier payment to this supplier with no confirmed outcome; it blocks a new one. */
  protected readonly pendingPayment = computed(() => {
    this.intents.revision();
    const supplierId = this.currentSupplierId();
    return supplierId
      ? this.intents.pending<MakePaymentRequest>(this.scope(), 'supplier-payment', supplierId)
      : null;
  });

  constructor() {
    effect(() => {
      if (
        !this.facade.lastMutationSucceeded() ||
        this.facade.lastMutationType() !== 'make-payment'
      ) {
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

  onCheckPendingPayment(): void {
    const intent = this.pendingPayment();
    const supplierId = this.currentSupplierId();
    if (!intent || !supplierId) {
      return;
    }
    this.isCheckingPayment.set(true);
    this.intents.check(this.scope(), intent).subscribe({
      next: (committed) => {
        this.isCheckingPayment.set(false);
        this.pendingNotice.set(
          committed ? 'financialOperations.confirmed' : 'financialOperations.notYetRecorded',
        );
        if (committed) {
          this.facade.loadLedger(supplierId);
        }
      },
      error: () => {
        this.isCheckingPayment.set(false);
        this.pendingNotice.set('financialOperations.checkFailed');
      },
    });
  }

  onRetryPendingPayment(): void {
    const supplierId = this.currentSupplierId();
    if (!supplierId) {
      return;
    }
    this.pendingNotice.set(null);
    this.facade.clearError();
    this.facade.clearMutationStatus();
    this.facade.retryPayment(supplierId);
  }

  protected readonly isLoading = this.facade.ledgerIsLoading;

  protected readonly tableEntries = computed(() => {
    this.currentLang(); // re-run on language change
    const currentBalanceDue = this.currentSupplier()?.balanceDue ?? 0;
    let rows = this.facade.ledgerEntries().map((entry: SupplierLedgerEntry) => {
      const isPayment = supplierLedgerEntryKind(entry.entryType) === 'PaymentMade';
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

    const sorted = [...rows].sort((a, b) => ((b.entryDate ?? '') > (a.entryDate ?? '') ? 1 : -1));

    let runningBalance = currentBalanceDue;
    return sorted.map((row) => {
      const balance = runningBalance;
      runningBalance = runningBalance - row.displayAmount;
      return { ...row, balance };
    });
  });

  getEntryTypeLabel(entryType: SupplierLedgerEntry['entryType']): string {
    switch (supplierLedgerEntryKind(entryType)) {
      case 'GoodsReceived':
        return this.transloco.translate('suppliers.entryTypes.goodsReceived');
      case 'PaymentMade':
        return this.transloco.translate('suppliers.entryTypes.paymentMade');
      case 'RecordAdjusted':
        return this.transloco.translate('suppliers.entryTypes.recordAdjusted');
      case 'Reversal':
        return this.transloco.translate('suppliers.entryTypes.reversal');
      case 'CostAdjusted':
        return this.transloco.translate('suppliers.entryTypes.costAdjusted');
      default:
        return String(entryType);
    }
  }

  getEntryTypeClass(entryType: SupplierLedgerEntry['entryType']): string {
    switch (supplierLedgerEntryKind(entryType)) {
      case 'GoodsReceived':
        return 'goods-received';
      case 'PaymentMade':
        return 'payment-made';
      case 'RecordAdjusted':
      case 'Reversal':
      case 'CostAdjusted':
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
