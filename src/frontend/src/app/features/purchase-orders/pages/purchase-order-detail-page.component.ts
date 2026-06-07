import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { FormsModule } from '@angular/forms';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';

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
  ],
  template: `
    <div class="page-container">
      <p-card>
        @if (facade.isLoadingDetail()) {
          <p-progressSpinner />
        } @else if (facade.selectedOrder(); as order) {
          <ng-template pTemplate="title">
            <div class="po-detail-header">
              <h2>{{ order.purchaseOrderNumber }}</h2>
              <div class="po-detail-actions">
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
              </div>
            </div>
          </ng-template>
          <p>
            <strong>{{ 'purchaseOrders.status' | transloco }}:</strong>
            <p-tag [value]="order.status" severity="info" />
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
                <th>{{ 'purchaseOrders.unitCost' | transloco }}</th>
                <th>{{ 'purchaseOrders.lineTotal' | transloco }}</th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-line>
              <tr>
                <td>{{ line.description }}</td>
                <td>{{ line.expectedQuantity }}</td>
                <td>{{ line.unitCost | number:'1.2-2' }}</td>
                <td>{{ line.lineTotal | number:'1.2-2' }}</td>
              </tr>
            </ng-template>
          </p-table>
          <p>
            <strong>{{ 'purchaseOrders.expectedTotal' | transloco }}:</strong>
            {{ order.expectedTotal | number:'1.2-2' }}
          </p>
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

  protected cancelReason = '';

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

  protected deleteDraft(purchaseOrderId: string): void {
    this.facade.deleteDraft(purchaseOrderId);
  }

  protected cancelOrder(purchaseOrderId: string): void {
    const reason = this.cancelReason.trim();
    if (!reason) return;
    this.facade.cancelOrder(purchaseOrderId, { reason });
    this.cancelReason = '';
  }
}
