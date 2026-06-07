import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';

@Component({
  selector: 'app-purchase-order-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    TranslocoPipe,
    CardModule,
    ProgressSpinnerModule,
    TableModule,
    TagModule,
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
              <a [routerLink]="['/inventory/purchase-orders', order.purchaseOrderId, 'edit']">
                {{ 'purchaseOrders.editPo' | transloco }}
              </a>
            </div>
          </ng-template>
          <p>
            <strong>{{ 'purchaseOrders.status' | transloco }}:</strong>
            <p-tag [value]="order.status" severity="info" />
          </p>
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
  `],
})
export class PurchaseOrderDetailPageComponent implements OnInit, OnDestroy {
  protected readonly facade = inject(PurchaseOrdersFacade);
  private readonly route = inject(ActivatedRoute);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('purchaseOrderId');
    if (id) {
      this.facade.loadDetail(id);
    }
  }

  ngOnDestroy(): void {
    this.facade.clearDetail();
  }
}
