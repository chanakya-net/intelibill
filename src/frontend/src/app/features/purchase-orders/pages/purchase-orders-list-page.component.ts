import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { CardModule } from 'primeng/card';
import { PaginatorModule } from 'primeng/paginator';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { DEFAULT_PURCHASE_ORDER_LIST_FILTERS, PurchaseOrderListFilters, PurchaseOrderStatus } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-orders-list-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslocoPipe,
    InputTextModule,
    InputNumberModule,
    CardModule,
    PaginatorModule,
    ProgressSpinnerModule,
    SelectModule,
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
            @if (permissions.canManagePurchaseOrders()) {
              <a routerLink="/inventory/purchase-orders/new">
                {{ 'purchaseOrders.newPo' | transloco }}
              </a>
            }
          </div>
        </ng-template>
        <div class="mb-4 flex flex-wrap gap-3">
          <input
            pInputText
            [ngModel]="filters.search"
            (ngModelChange)="onSearchChange($event)"
            [placeholder]="'purchaseOrders.searchPlaceholder' | transloco"
          />
          <p-select
            [options]="statusOptions"
            optionLabel="label"
            optionValue="value"
            [ngModel]="filters.status"
            (ngModelChange)="onStatusChange($event)"
            appendTo="body"
          />
          <input type="date" [ngModel]="filters.orderDateFrom" (ngModelChange)="onOrderDateFromChange($event)" />
          <input type="date" [ngModel]="filters.orderDateTo" (ngModelChange)="onOrderDateToChange($event)" />
          <p-button
            [label]="'common.clear' | transloco"
            severity="secondary"
            [text]="true"
            (onClick)="clearFilters()"
          />
        </div>
        @if (facade.isLoadingList()) {
          <p-progressSpinner />
        } @else {
          <p-table [value]="[...facade.orders()]" dataKey="purchaseOrderId">
            <ng-template pTemplate="header">
              <tr>
                <th>{{ 'purchaseOrders.poNumber' | transloco }}</th>
                <th>{{ 'purchaseOrders.status' | transloco }}</th>
                <th>{{ 'purchaseOrders.receivedProgress' | transloco }}</th>
                <th>{{ 'purchaseOrders.lineCount' | transloco }}</th>
                <th>{{ 'purchaseOrders.expectedTotal' | transloco }}</th>
                <th>{{ 'purchaseOrders.createdAt' | transloco }}</th>
                <th></th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-order>
              <tr class="cursor-pointer" (click)="openOrder(order.purchaseOrderId)">
                <td>{{ order.purchaseOrderNumber }}</td>
                <td>
                  <p-tag [value]="order.status" severity="info" />
                </td>
                <td>{{ receivedProgress(order.receivedQuantity, order.expectedQuantity) }}</td>
                <td>{{ order.lineCount }}</td>
                <td>{{ order.expectedTotal | number:'1.2-2' }}</td>
                <td>{{ order.createdAt | date:'short' }}</td>
                <td>
                  @if (permissions.canManagePurchaseOrders() && order.status === 'Draft') {
                    <a href="" (click)="openEditOrder(order.purchaseOrderId, $event)">
                      {{ 'purchaseOrders.editPo' | transloco }}
                    </a>
                  }
                </td>
              </tr>
            </ng-template>
            <ng-template pTemplate="emptymessage">
              <tr>
                <td colspan="7">{{ 'purchaseOrders.noResults' | transloco }}</td>
              </tr>
            </ng-template>
          </p-table>
          <p-paginator
            [first]="first"
            [rows]="pagination.pageSize"
            [totalRecords]="pagination.totalCount"
            [rowsPerPageOptions]="[20, 50, 100]"
            (onPageChange)="onPageChange($event)"
          />
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
  protected readonly permissions = inject(ShopPermissionsService);
  private readonly router = inject(Router);

  protected readonly statusOptions: { label: string; value: PurchaseOrderStatus | '' }[] = [
    { label: 'All', value: '' },
    { label: 'Draft', value: 'Draft' },
    { label: 'Placed', value: 'Placed' },
    { label: 'Cancelled', value: 'Cancelled' },
  ];

  protected get filters(): PurchaseOrderListFilters {
    return this.facade.filters();
  }

  protected get pagination(): { totalCount: number; pageNumber: number; pageSize: number } {
    return this.facade.pagination();
  }

  protected get first(): number {
    return Math.max(0, (this.pagination.pageNumber - 1) * this.pagination.pageSize);
  }

  ngOnInit(): void {
    this.facade.loadOrders(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
  }

  protected onSearchChange(search: string): void {
    this.facade.loadOrders({ search, page: 1 });
  }

  protected onStatusChange(status: PurchaseOrderStatus | ''): void {
    this.facade.loadOrders({ status, page: 1 });
  }

  protected onOrderDateFromChange(orderDateFrom: string): void {
    this.facade.loadOrders({ orderDateFrom, page: 1 });
  }

  protected onOrderDateToChange(orderDateTo: string): void {
    this.facade.loadOrders({ orderDateTo, page: 1 });
  }

  protected clearFilters(): void {
    this.facade.resetListFilters();
    this.facade.loadOrders(DEFAULT_PURCHASE_ORDER_LIST_FILTERS);
  }

  protected onPageChange(event: { page?: number; rows?: number }): void {
    const pageSize = event.rows ?? this.pagination.pageSize;
    const page = pageSize !== this.pagination.pageSize ? 1 : (event.page ?? 0) + 1;
    this.facade.loadOrders({ page, pageSize });
  }

  protected openOrder(purchaseOrderId: string): void {
    void this.router.navigate(['/inventory/purchase-orders', purchaseOrderId]);
  }

  protected openEditOrder(purchaseOrderId: string, event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    void this.router.navigate(['/inventory/purchase-orders', purchaseOrderId, 'edit']);
  }

  protected receivedProgress(receivedQuantity: number, expectedQuantity: number): string {
    return `${receivedQuantity} / ${expectedQuantity}`;
  }
}
