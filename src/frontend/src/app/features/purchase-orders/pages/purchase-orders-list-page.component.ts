import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { TranslocoService } from '@ngneat/transloco';

import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { CardModule } from 'primeng/card';
import { ConfirmationService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DatePickerModule } from 'primeng/datepicker';
import { PaginatorModule } from 'primeng/paginator';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import { formatLocalIsoDate, parseDateOnlyAsLocalDate } from '../../../shared/utils/date-time.util';
import { PurchaseOrdersFacade } from '../state/purchase-orders.facade';
import { DEFAULT_PURCHASE_ORDER_LIST_FILTERS, PurchaseOrderListFilters, PurchaseOrderStatus } from '../services/purchase-order.service';
import { PurchaseOrderBuilderPageComponent } from './purchase-order-builder-page.component';

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
    ConfirmDialogModule,
    DatePickerModule,
    PaginatorModule,
    ProgressSpinnerModule,
    SelectModule,
    TableModule,
    TagModule,
    ButtonModule,
    PurchaseOrderBuilderPageComponent,
  ],
  providers: [ConfirmationService],
  template: `
    <div class="page-container">
      <p-confirmDialog />
      <p-card>
        <ng-template pTemplate="title">
          <div class="po-list-header">
            <h2>{{ 'purchaseOrders.title' | transloco }}</h2>
            @if (permissions.canManagePurchaseOrders()) {
              <button
                pButton
                type="button"
                class="po-list-header__new-button"
                icon="pi pi-plus"
                [label]="'purchaseOrders.newPo' | transloco"
                (click)="openNewOrder()"
              ></button>
            }
          </div>
        </ng-template>
        <div class="po-filter-bar">
          <input
            pInputText
            class="po-filter-bar__search"
            [ngModel]="filters.search"
            (ngModelChange)="onSearchChange($event)"
            [placeholder]="'purchaseOrders.searchPlaceholder' | transloco"
          />
          <p-select
            styleClass="min-h-11 min-w-32 rounded-xl border border-slate-300 bg-white shadow-sm"
            [options]="statusOptions"
            optionLabel="label"
            optionValue="value"
            [ngModel]="filters.status"
            (ngModelChange)="onStatusChange($event)"
            appendTo="body"
          />
          <p-datepicker
            ngSkipHydration
            styleClass="po-filter-bar__date"
            inputStyleClass="w-full min-h-11 rounded-xl border border-slate-300 bg-white px-4 text-slate-800 shadow-sm focus:border-orange-600 focus:ring-2 focus:ring-orange-600/20"
            [ngModel]="orderDateFromValue()"
            (ngModelChange)="onOrderDateFromChange($event)"
            dateFormat="dd/mm/yy"
            [showIcon]="true"
            iconDisplay="input"
            appendTo="body"
            [placeholder]="'dd/mm/yyyy'"
          />
          <p-datepicker
            ngSkipHydration
            styleClass="po-filter-bar__date"
            inputStyleClass="w-full min-h-11 rounded-xl border border-slate-300 bg-white px-4 text-slate-800 shadow-sm focus:border-orange-600 focus:ring-2 focus:ring-orange-600/20"
            [ngModel]="orderDateToValue()"
            (ngModelChange)="onOrderDateToChange($event)"
            dateFormat="dd/mm/yy"
            [showIcon]="true"
            iconDisplay="input"
            appendTo="body"
            [placeholder]="'dd/mm/yyyy'"
          />
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
                <th>{{ 'purchaseOrders.statusLabel' | transloco }}</th>
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
                  <p-tag [value]="'purchaseOrders.status.' + order.status | transloco" severity="info" />
                </td>
                <td>{{ receivedProgress(order.receivedQuantity, order.expectedQuantity) }}</td>
                <td>{{ order.lineCount }}</td>
                <td>{{ order.expectedTotal | number:'1.2-2' }}</td>
                <td>{{ order.createdAt | date:'short' }}</td>
                <td>
                  @if (permissions.canManagePurchaseOrders() && order.status === 'Draft') {
                    <div class="po-row-actions">
                      <button
                        pButton
                        type="button"
                        severity="secondary"
                        icon="pi pi-pencil"
                        [label]="'purchaseOrders.editPo' | transloco"
                        (click)="openEditOrder(order.purchaseOrderId, $event)"
                      ></button>
                      <button
                        pButton
                        type="button"
                        severity="success"
                        icon="pi pi-send"
                        [label]="'purchaseOrders.actions.placeOrder' | transloco"
                        (click)="placeOrder(order.purchaseOrderId, $event)"
                      ></button>
                      <button
                        pButton
                        type="button"
                        severity="danger"
                        icon="pi pi-trash"
                        [label]="'purchaseOrders.actions.deleteDraft' | transloco"
                        (click)="deleteDraft(order.purchaseOrderId, $event)"
                      ></button>
                    </div>
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

    @if (showBuilderOverlay()) {
      <app-purchase-order-builder-page
        [purchaseOrderId]="editingPoId()"
        (closeRequested)="closeBuilder()"
      />
    }
  `,
  styles: [`
    .po-list-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
    .po-list-header__new-button { white-space: nowrap; }
    .po-filter-bar { display: flex; align-items: center; flex-wrap: wrap; gap: .75rem; margin-bottom: 1rem; }
    .po-filter-bar__search {
      width: min(24rem, 100%);
      min-height: 2.75rem;
      border: 1px solid #cbd5e1;
      border-radius: .75rem;
      background: #fff;
      color: #1f2937;
      box-shadow: 0 1px 2px rgba(15, 23, 42, 0.06);
    }
    .po-filter-bar__search:enabled:focus {
      border-color: #ea580c;
      box-shadow: 0 0 0 3px rgba(234, 88, 12, 0.16);
    }
    .po-filter-bar__date { width: 12.5rem; }
    .po-row-actions { display: flex; align-items: center; justify-content: flex-end; gap: .5rem; flex-wrap: wrap; }
  `],
})
export class PurchaseOrdersListPageComponent implements OnInit {
  protected readonly facade = inject(PurchaseOrdersFacade);
  protected readonly permissions = inject(ShopPermissionsService);
  private readonly router = inject(Router);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly translocoService = inject(TranslocoService);

  protected readonly showBuilderOverlay = signal(false);
  protected readonly editingPoId = signal<string | null>(null);
  protected readonly orderDateFromValue = signal<Date | null>(null);
  protected readonly orderDateToValue = signal<Date | null>(null);

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

  protected openNewOrder(): void {
    this.editingPoId.set(null);
    this.showBuilderOverlay.set(true);
  }

  protected onSearchChange(search: string): void {
    this.facade.loadOrders({ search, page: 1 });
  }

  protected onStatusChange(status: PurchaseOrderStatus | ''): void {
    this.facade.loadOrders({ status, page: 1 });
  }

  protected onOrderDateFromChange(orderDateFrom: Date | string | null): void {
    this.orderDateFromValue.set(this.toDatePickerValue(orderDateFrom));
    this.facade.loadOrders({ orderDateFrom: this.toFilterDateValue(orderDateFrom), page: 1 });
  }

  protected onOrderDateToChange(orderDateTo: Date | string | null): void {
    this.orderDateToValue.set(this.toDatePickerValue(orderDateTo));
    this.facade.loadOrders({ orderDateTo: this.toFilterDateValue(orderDateTo), page: 1 });
  }

  protected clearFilters(): void {
    this.orderDateFromValue.set(null);
    this.orderDateToValue.set(null);
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
    event.stopPropagation();
    this.editingPoId.set(purchaseOrderId);
    this.showBuilderOverlay.set(true);
  }

  protected deleteDraft(purchaseOrderId: string, event: Event): void {
    event.stopPropagation();
    this.confirmationService.confirm({
      message: this.translocoService.translate('purchaseOrders.dialog.deleteDraftBody'),
      header: this.translocoService.translate('purchaseOrders.dialog.deleteDraftTitle'),
      icon: 'pi pi-exclamation-triangle',
      acceptButtonStyleClass: 'p-button-danger',
      rejectButtonStyleClass: 'p-button-secondary p-button-text',
      accept: () => this.confirmDeleteDraft(purchaseOrderId),
    });
  }

  protected placeOrder(purchaseOrderId: string, event: Event): void {
    event.stopPropagation();
    this.facade.placeOrder(purchaseOrderId);
  }

  protected confirmDeleteDraft(purchaseOrderId: string): void {
    this.facade.deleteDraft(purchaseOrderId);
  }

  protected closeBuilder(): void {
    this.showBuilderOverlay.set(false);
    this.editingPoId.set(null);
    this.facade.loadOrders(this.facade.filters());
  }

  protected receivedProgress(receivedQuantity: number, expectedQuantity: number): string {
    return `${receivedQuantity} / ${expectedQuantity}`;
  }

  private toDatePickerValue(value: Date | string | null): Date | null {
    if (value instanceof Date) {
      return value;
    }

    return value ? parseDateOnlyAsLocalDate(value) : null;
  }

  private toFilterDateValue(value: Date | string | null): string {
    if (!value) {
      return '';
    }

    if (typeof value === 'string') {
      return value;
    }

    return formatLocalIsoDate(value);
  }
}
