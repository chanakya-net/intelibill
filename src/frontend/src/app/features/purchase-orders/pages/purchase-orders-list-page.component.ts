import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';

import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';

@Component({
  selector: 'app-purchase-orders-list-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    TranslocoPipe,
    CardModule,
    ProgressSpinnerModule,
    TableModule,
    TagModule,
    ButtonModule,
  ],
  template: `
    <div class="page-container">
      <p-card>
        <ng-template pTemplate="title">
          <div class="po-list-header">
            <h2>{{ 'purchaseOrders.title' | transloco }}</h2>
            <a routerLink="/inventory/purchase-orders/new">
              {{ 'purchaseOrders.newPo' | transloco }}
            </a>
          </div>
        </ng-template>
        @if (facade.isLoadingList()) {
          <p-progressSpinner />
        } @else {
          <p-table [value]="[...facade.orders()]" dataKey="purchaseOrderId" [rows]="20">
            <ng-template pTemplate="header">
              <tr>
                <th>{{ 'purchaseOrders.poNumber' | transloco }}</th>
                <th>{{ 'purchaseOrders.status' | transloco }}</th>
                <th>{{ 'purchaseOrders.lineCount' | transloco }}</th>
                <th>{{ 'purchaseOrders.expectedTotal' | transloco }}</th>
                <th>{{ 'purchaseOrders.createdAt' | transloco }}</th>
                <th></th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-order>
              <tr>
                <td>
                  <a [routerLink]="['/inventory/purchase-orders', order.purchaseOrderId]">
                    {{ order.purchaseOrderNumber }}
                  </a>
                </td>
                <td>
                  <p-tag [value]="order.status" severity="info" />
                </td>
                <td>{{ order.lineCount }}</td>
                <td>{{ order.expectedTotal | number:'1.2-2' }}</td>
                <td>{{ order.createdAt | date:'short' }}</td>
                <td>
                  <a [routerLink]="['/inventory/purchase-orders', order.purchaseOrderId, 'edit']">
                    {{ 'purchaseOrders.editPo' | transloco }}
                  </a>
                </td>
              </tr>
            </ng-template>
            <ng-template pTemplate="emptymessage">
              <tr>
                <td colspan="6">{{ 'purchaseOrders.noDrafts' | transloco }}</td>
              </tr>
            </ng-template>
          </p-table>
        }
      </p-card>
    </div>
  `,
  styles: [`
    .po-list-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
  `],
})
export class PurchaseOrdersListPageComponent implements OnInit {
  protected readonly facade = inject(PurchaseOrdersFacade);

  ngOnInit(): void {
    this.facade.loadOrders();
  }
}
