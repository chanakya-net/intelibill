import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { TranslocoService } from '@ngneat/transloco';

import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';
import { FormsModule } from '@angular/forms';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { ReceivePurchaseOrderDialogComponent } from '../components/receive-purchase-order-dialog.component';
import { ReceivePurchaseOrderRequest } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    TranslocoPipe,
    CardModule,
    ProgressSpinnerModule,
    TableModule,
    TagModule,
    ButtonModule,
    InputTextModule,
    ConfirmDialogModule,
    ReceivePurchaseOrderDialogComponent,
  ],
  providers: [ConfirmationService],
  template: `
    <div class="page-container">
      <p-confirmDialog />
      <p-card>
        @if (facade.isLoadingDetail()) {
          <p-progressSpinner />
        } @else if (facade.selectedOrder(); as order) {
          <ng-template pTemplate="title">
            <div class="po-detail-header">
              <h2>{{ order.purchaseOrderNumber }}</h2>
              <div class="po-detail-actions">
                <button type="button" (click)="openPrintView(order.purchaseOrderId)">
                  {{ 'purchaseOrders.actions.print' | transloco }}
                </button>
                @if (permissions.canManagePurchaseOrders() && order.status === 'Draft') {
                  <a [routerLink]="['/inventory/purchase-orders', order.purchaseOrderId, 'edit']">
                    {{ 'purchaseOrders.editPo' | transloco }}
                  </a>
                  <button type="button" (click)="placeOrder(order.purchaseOrderId)">
                    {{ 'purchaseOrders.actions.placeOrder' | transloco }}
                  </button>
                  <button type="button" (click)="deleteDraft(order.purchaseOrderId)">
                    {{ 'purchaseOrders.actions.deleteDraft' | transloco }}
                  </button>
                }
                @if (permissions.canManagePurchaseOrders() && order.status === 'Placed') {
                  <button type="button" (click)="showReceiveDialog.set(true)">
                    {{ 'purchaseOrders.actions.receive' | transloco }}
                  </button>
                  <div class="po-cancel-form">
                    <input
                      pInputText
                      [(ngModel)]="cancelReason"
                      [placeholder]="'purchaseOrders.dialog.cancelReasonLabel' | transloco"
                    />
                    <button type="button" [disabled]="!cancelReason.trim()" (click)="cancelOrder(order.purchaseOrderId)">
                      {{ 'purchaseOrders.actions.cancelOrder' | transloco }}
                    </button>
                  </div>
                }
                @if (permissions.canManagePurchaseOrders() && order.status === 'PartiallyReceived') {
                  <button type="button" (click)="showReceiveDialog.set(true)">
                    {{ 'purchaseOrders.actions.receive' | transloco }}
                  </button>
                }
              </div>
            </div>
          </ng-template>
          @if (permissions.canManagePurchaseOrders() && (order.status === 'Placed' || order.status === 'PartiallyReceived')) {
            <app-receive-purchase-order-dialog
              [order]="order"
              [visible]="showReceiveDialog()"
              (visibleChange)="showReceiveDialog.set($event)"
              [submitting]="facade.isSubmitting()"
              (receive)="receiveOrder(order.purchaseOrderId, $event)"
            />
          }
          <p>
            <strong>{{ 'purchaseOrders.statusLabel' | transloco }}:</strong>
            <p-tag [value]="'purchaseOrders.status.' + order.status | transloco" severity="info" />
          </p>
          @if (order.cancellationReason) {
            <p>
              <strong>{{ 'purchaseOrders.cancellationReason' | transloco }}:</strong>
              {{ order.cancellationReason }}
            </p>
          }
          @if (order.supplierId) {
            <p>
              <strong>{{ 'purchaseOrders.builder.supplier' | transloco }}:</strong>
              {{ order.supplierId }}
            </p>
          }
          @if (order.orderDate) {
            <p>
              <strong>{{ 'purchaseOrders.builder.orderDate' | transloco }}:</strong>
              {{ order.orderDate }}
            </p>
          }
          @if (order.expectedDeliveryDate) {
            <p>
              <strong>{{ 'purchaseOrders.builder.expectedDeliveryDate' | transloco }}:</strong>
              {{ order.expectedDeliveryDate }}
            </p>
          }
          @if (order.supplierReferenceNumber) {
            <p>
              <strong>{{ 'purchaseOrders.builder.supplierReferenceNumber' | transloco }}:</strong>
              {{ order.supplierReferenceNumber }}
            </p>
          }
          @if (order.notes) {
            <p>
              <strong>{{ 'purchaseOrders.notes' | transloco }}:</strong>
              {{ order.notes }}
            </p>
          }
          <p-table [value]="[...order.lines]" dataKey="lineId">
            <ng-template pTemplate="header">
              <tr>
                <th>{{ 'purchaseOrders.lineDescription' | transloco }}</th>
                <th>{{ 'purchaseOrders.expectedQuantity' | transloco }}</th>
                <th>{{ 'purchaseOrders.receivedQuantity' | transloco }}</th>
                <th>{{ 'purchaseOrders.remainingQuantity' | transloco }}</th>
                <th>{{ 'purchaseOrders.unitCost' | transloco }}</th>
                <th>{{ 'purchaseOrders.lineTotal' | transloco }}</th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-line>
              <tr>
                <td>{{ line.description }}</td>
                <td>{{ line.expectedQuantity }}</td>
                <td>{{ line.receivedQuantity ?? 0 }}</td>
                <td>{{ line.remainingQuantity ?? line.expectedQuantity }}</td>
                <td>{{ line.unitCost | number:'1.2-2' }}</td>
                <td>{{ line.lineTotal | number:'1.2-2' }}</td>
              </tr>
            </ng-template>
          </p-table>
          <p>
            <strong>{{ 'purchaseOrders.expectedTotal' | transloco }}:</strong>
            {{ order.expectedTotal | number:'1.2-2' }}
          </p>
          @if ((order.receipts?.length ?? 0) > 0) {
            <h3>{{ 'purchaseOrders.receipts.title' | transloco }}</h3>
            <p-table [value]="[...(order.receipts ?? [])]" dataKey="receiptId">
              <ng-template pTemplate="header">
                <tr>
                  <th>{{ 'purchaseOrders.receipts.receipt' | transloco }}</th>
                  <th>{{ 'purchaseOrders.receipts.receivedAt' | transloco }}</th>
                  <th>{{ 'purchaseOrders.receipts.quantity' | transloco }}</th>
                  <th>{{ 'purchaseOrders.receipts.totalCost' | transloco }}</th>
                </tr>
              </ng-template>
              <ng-template pTemplate="body" let-receipt>
                <tr>
                  <td>{{ receipt.receiptNumber }}</td>
                  <td>{{ receipt.receivedAt }}</td>
                  <td>{{ receipt.lines[0]?.quantity }}</td>
                  <td>{{ receipt.lines[0]?.totalPurchaseCost | number:'1.2-2' }}</td>
                </tr>
              </ng-template>
            </p-table>
          }
        } @else if (facade.errorMessage()) {
          <p>{{ facade.errorMessage() }}</p>
        }
      </p-card>
    </div>
  `,
  styles: [`
    .po-detail-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
    .po-detail-actions { display: flex; align-items: center; gap: .5rem; flex-wrap: wrap; }
    .po-cancel-form { display: flex; align-items: center; gap: .5rem; }
  `],
})
export class PurchaseOrderDetailPageComponent implements OnInit, OnDestroy {
  protected readonly facade = inject(PurchaseOrdersFacade);
  protected readonly permissions = inject(ShopPermissionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly translocoService = inject(TranslocoService);

  protected cancelReason = '';
  protected showReceiveDialog = signal(false);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('purchaseOrderId');
    if (id) {
      this.facade.loadDetail(id);
    }
  }

  ngOnDestroy(): void {
    this.facade.clearDetail();
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

  protected receiveOrder(purchaseOrderId: string, payload: ReceivePurchaseOrderRequest): void {
    this.facade.receiveOrder(purchaseOrderId, payload);
    this.showReceiveDialog.set(false);
  }
}
