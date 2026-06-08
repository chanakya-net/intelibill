import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { TranslocoService } from '@ngneat/transloco';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { ReceivePurchaseOrderDialogComponent } from '../components/receive-purchase-order-dialog.component';
import { PurchaseOrderReceiptHistoryComponent } from '../components/purchase-order-receipt-history.component';
import {
  PurchaseOrderDetail,
  PurchaseOrderStatus,
  ReceivePurchaseOrderRequest,
} from '../services/purchase-order.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { PurchaseOrderBuilderPageComponent } from './purchase-order-builder-page.component';

@Component({
  selector: 'app-purchase-order-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslocoPipe,
    ProgressSpinnerModule,
    TableModule,
    ButtonModule,
    InputTextModule,
    ConfirmDialogModule,
    ReceivePurchaseOrderDialogComponent,
    PurchaseOrderReceiptHistoryComponent,
    PurchaseOrderBuilderPageComponent,
  ],
  providers: [ConfirmationService],
  templateUrl: './purchase-order-detail-page.component.html',
  styleUrl: './purchase-order-detail-page.component.scss',
})
export class PurchaseOrderDetailPageComponent implements OnInit, OnDestroy {
  protected readonly facade = inject(PurchaseOrdersFacade);
  protected readonly permissions = inject(ShopPermissionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly translocoService = inject(TranslocoService);

  protected cancelReason = '';
  protected closeReason = '';
  protected showReceiveDialog = signal(false);
  protected showEditOverlay = signal(false);
  protected editingPoId = signal<string | null>(null);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('purchaseOrderId');
    if (id) {
      this.facade.loadDetail(id);
    }
  }

  ngOnDestroy(): void {
    this.facade.clearDetail();
  }

  protected goBack(): void {
    void this.router.navigate(['/inventory/purchase-orders']);
  }

  protected summaryCards(order: PurchaseOrderDetail): Array<{
    labelKey: string;
    value: number;
    variant: 'count' | 'money' | 'progress';
    tone: string;
  }> {
    return [
      { labelKey: 'purchaseOrders.lineCount', value: order.lines.length, variant: 'count', tone: 'amber' },
      { labelKey: 'purchaseOrders.summary.receivedProgress', value: 0, variant: 'progress', tone: 'terracotta' },
      { labelKey: 'purchaseOrders.expectedTotal', value: order.expectedTotal, variant: 'money', tone: 'sage' },
      { labelKey: 'purchaseOrders.receipts.title', value: order.receipts?.length ?? 0, variant: 'count', tone: 'ink' },
    ];
  }

  protected totalExpectedQuantity(order: PurchaseOrderDetail): number {
    return order.lines.reduce((total, line) => total + line.expectedQuantity, 0);
  }

  protected totalReceivedQuantity(order: PurchaseOrderDetail): number {
    return order.lines.reduce((total, line) => total + (line.receivedQuantity ?? 0), 0);
  }

  protected statusTone(status: PurchaseOrderStatus): string {
    if (status === 'PartiallyReceived') return 'partial';
    return status.toLowerCase();
  }

  protected openEditOverlay(purchaseOrderId: string): void {
    this.editingPoId.set(purchaseOrderId);
    this.showEditOverlay.set(true);
  }

  protected closeEditOverlay(): void {
    const id = this.editingPoId();
    this.showEditOverlay.set(false);
    this.editingPoId.set(null);
    if (id) this.facade.loadDetail(id);
  }

  protected placeOrder(purchaseOrderId: string): void {
    this.facade.placeOrder(purchaseOrderId);
  }

  protected openPrintView(purchaseOrderId: string): void {
    window.open(`/inventory/purchase-orders/${purchaseOrderId}/print`, '_blank');
  }

  protected deleteDraft(purchaseOrderId: string): void {
    this.confirmationService.confirm({
      message: this.translocoService.translate('purchaseOrders.dialog.deleteDraftBody'),
      header: this.translocoService.translate('purchaseOrders.dialog.deleteDraftTitle'),
      icon: 'pi pi-exclamation-triangle',
      acceptButtonStyleClass: 'p-button-danger',
      rejectButtonStyleClass: 'p-button-secondary p-button-text',
      accept: () => {
        this.facade.deleteDraft(purchaseOrderId);
      },
    });
  }

  protected cancelOrder(purchaseOrderId: string): void {
    const reason = this.cancelReason.trim();
    if (!reason) return;
    this.facade.cancelOrder(purchaseOrderId, { reason });
    this.cancelReason = '';
  }

  protected closeOrder(purchaseOrderId: string): void {
    const reason = this.closeReason.trim();
    if (!reason) return;
    this.facade.closeOrder(purchaseOrderId, { reason });
    this.closeReason = '';
  }

  protected receiveOrder(purchaseOrderId: string, payload: ReceivePurchaseOrderRequest): void {
    this.facade.receiveOrder(purchaseOrderId, payload);
    this.showReceiveDialog.set(false);
  }
}
