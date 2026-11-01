import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { AuthService } from '../../../core/auth/auth.service';
import { FinancialOperationIntentService } from '../../../core/services/financial-operation-intent.service';
import { PendingFinancialOperationComponent } from '../../../shared/components/pending-financial-operation/pending-financial-operation.component';
import { Supplier } from '../services/supplier.service';
import { SuppliersFacade } from '../state/suppliers.facade';
import type { MakePaymentRequest } from '../services/supplier-ledger.service';
import { SupplierPaymentFormComponent } from './supplier-detail/supplier-payment-form.component';

@Component({
  selector: 'app-make-payment-overlay',
  standalone: true,
  imports: [PendingFinancialOperationComponent, SupplierPaymentFormComponent, TranslocoPipe],
  templateUrl: './make-payment-overlay.component.html',
  styleUrl: './make-payment-overlay.component.scss',
})
export class MakePaymentOverlayComponent implements OnInit {
  private readonly suppliersFacade = inject(SuppliersFacade);
  private readonly intents = inject(FinancialOperationIntentService);
  private readonly authService = inject(AuthService);

  readonly isSubmitting = this.suppliersFacade.isSubmitting;
  readonly serverError = this.suppliersFacade.errorMessage;

  @Input({ required: true }) supplier!: Supplier;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly today = new Date();
  readonly pendingNotice = signal<string | null>(null);
  readonly isCheckingPayment = signal(false);
  private readonly scope = computed(() => ({
    userId: this.authService.session()?.user?.id ?? '',
    shopId: this.authService.session()?.activeShopId ?? '',
  }));
  /** An earlier payment to this supplier with no confirmed outcome; it blocks a new one. */
  readonly pendingPayment = computed(() => {
    this.intents.revision();
    return this.intents.pending<MakePaymentRequest>(this.scope(), 'supplier-payment', this.supplier.supplierId);
  });

  ngOnInit(): void {
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.closeRequested.emit();
  }

  onPaymentSubmitted(payload: MakePaymentRequest): void {
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.suppliersFacade.makePayment(this.supplier.supplierId, payload);
  }

  onCheckPendingPayment(): void {
    const intent = this.pendingPayment();
    if (!intent) {
      return;
    }
    this.isCheckingPayment.set(true);
    this.intents.check(this.scope(), intent).subscribe({
      next: (committed) => {
        this.isCheckingPayment.set(false);
        this.pendingNotice.set(committed ? 'financialOperations.confirmed' : 'financialOperations.notYetRecorded');
        if (committed) {
          this.suppliersFacade.load();
        }
      },
      error: () => {
        this.isCheckingPayment.set(false);
        this.pendingNotice.set('financialOperations.checkFailed');
      },
    });
  }

  onRetryPendingPayment(): void {
    this.pendingNotice.set(null);
    this.suppliersFacade.clearError();
    this.suppliersFacade.clearMutationStatus();
    this.suppliersFacade.retryPayment(this.supplier.supplierId);
  }
}
