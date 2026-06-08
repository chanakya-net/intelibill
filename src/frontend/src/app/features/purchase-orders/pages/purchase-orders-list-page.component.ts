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
import {
  DEFAULT_PURCHASE_ORDER_LIST_FILTERS,
  PurchaseOrderDetail,
  PurchaseOrderListFilters,
  PurchaseOrderStatus,
  ReceivePurchaseOrderRequest,
} from '../services/purchase-order.service';
import { ReceivePurchaseOrderDialogComponent } from '../components/receive-purchase-order-dialog.component';
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
    ReceivePurchaseOrderDialogComponent,
    PurchaseOrderBuilderPageComponent,
  ],
  providers: [ConfirmationService],
  template: `
    <section class="po-page">
      <p-confirmDialog />

      <header class="po-hero">
        <div class="po-hero__copy">
          <p class="eyebrow">{{ 'purchaseOrders.directory' | transloco }}</p>
          <h1>{{ 'purchaseOrders.title' | transloco }}</h1>
          <p class="subtitle">{{ 'purchaseOrders.subtitle' | transloco }}</p>
          <div class="po-hero__meta">
            <span class="meta-chip meta-chip--primary">
              {{ 'purchaseOrders.showingCount' | transloco:{ visible: facade.orders().length, total: pagination.totalCount } }}
            </span>
            <span class="meta-chip">{{ 'purchaseOrders.summary.pendingReceipt' | transloco }}: {{ pendingReceiptCount() }}</span>
            <span class="meta-chip">{{ 'purchaseOrders.receivedProgress' | transloco }}: {{ totalReceivedQuantity() }} / {{ totalExpectedQuantity() }}</span>
          </div>
        </div>

        @if (permissions.canManagePurchaseOrders()) {
          <div class="po-hero__actions">
            <button
              pButton
              type="button"
              class="po-list-header__new-button"
              icon="pi pi-plus"
              [label]="'purchaseOrders.newPo' | transloco"
              (click)="openNewOrder()"
            ></button>
          </div>
        }
      </header>

      <div class="summary-grid" aria-label="Purchase order summary">
        @for (card of summaryCards(); track card.labelKey) {
          <article class="summary-card" [ngClass]="'summary-card--' + card.tone">
            <div class="summary-card__label">{{ card.labelKey | transloco }}</div>
            <div class="summary-card__value">
              @if (card.variant === 'money') {
                {{ card.value | number:'1.2-2' }}
              } @else if (card.variant === 'progress') {
                {{ totalReceivedQuantity() }} / {{ totalExpectedQuantity() }}
              } @else {
                {{ card.value | number:'1.0-0' }}
              }
            </div>
          </article>
        }
      </div>

      @if (facade.isLoadingList()) {
        <section class="directory-panel directory-panel--loading" aria-busy="true">
          <div class="loading">
            <p-progressSpinner />
          </div>
        </section>
      } @else {
        <section class="directory-panel">
          <header class="directory-panel__header">
            <div>
              <p class="directory-panel__eyebrow">{{ 'purchaseOrders.directory' | transloco }}</p>
              <h2>{{ 'purchaseOrders.title' | transloco }}</h2>
              <p class="directory-panel__subtitle">{{ 'purchaseOrders.subtitle' | transloco }}</p>
            </div>

            <div class="directory-panel__metrics" aria-label="Purchase order directory metrics">
              @for (metric of directoryMetrics(); track metric.labelKey) {
                <span class="metric-chip">
                  <strong>
                    @if (metric.variant === 'money') {
                      {{ metric.value | number:'1.2-2' }}
                    } @else {
                      {{ metric.value | number:'1.0-0' }}
                    }
                  </strong>
                  <span>{{ metric.labelKey | transloco }}</span>
                </span>
              }
            </div>
          </header>

          <div class="po-filter-bar">
            <input
              pInputText
              class="po-filter-bar__search"
              [ngModel]="filters.search"
              (ngModelChange)="onSearchChange($event)"
              [placeholder]="'purchaseOrders.searchPlaceholder' | transloco"
            />
            <p-select
              styleClass="po-filter-bar__select"
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
              inputStyleClass="po-filter-bar__date-input"
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
              inputStyleClass="po-filter-bar__date-input"
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

          <div class="directory-panel__surface">
            <p-table [value]="[...facade.orders()]" dataKey="purchaseOrderId">
              <ng-template pTemplate="header">
                <tr>
                  <th>{{ 'purchaseOrders.poNumber' | transloco }}</th>
                  <th>{{ 'purchaseOrders.statusLabel' | transloco }}</th>
                  <th>{{ 'purchaseOrders.receivedProgress' | transloco }}</th>
                  <th>{{ 'purchaseOrders.lineCount' | transloco }}</th>
                  <th>{{ 'purchaseOrders.expectedTotal' | transloco }}</th>
                  <th>{{ 'purchaseOrders.createdAt' | transloco }}</th>
                  <th>{{ 'purchaseOrders.actionsLabel' | transloco }}</th>
                </tr>
              </ng-template>
              <ng-template pTemplate="body" let-order>
                <tr class="po-table-row" (click)="openOrder(order.purchaseOrderId)">
                  <td class="po-number">{{ order.purchaseOrderNumber }}</td>
                  <td>
                    <span class="po-status-pill" [ngClass]="'po-status-pill--' + statusTone(order.status)">
                      {{ 'purchaseOrders.status.' + order.status | transloco }}
                    </span>
                  </td>
                  <td>{{ receivedProgress(order.receivedQuantity, order.expectedQuantity) }}</td>
                  <td>{{ order.lineCount }}</td>
                  <td class="money">{{ order.expectedTotal | number:'1.2-2' }}</td>
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
                    } @else if (permissions.canManagePurchaseOrders() && (order.status === 'Placed' || order.status === 'PartiallyReceived')) {
                      <div class="po-row-actions">
                        <button
                          pButton
                          type="button"
                          severity="success"
                          icon="pi pi-inbox"
                          [label]="'purchaseOrders.actions.receive' | transloco"
                          (click)="openReceiveOrder(order.purchaseOrderId, $event)"
                        ></button>
                      </div>
                    }
                  </td>
                </tr>
              </ng-template>
              <ng-template pTemplate="emptymessage">
                <tr>
                  <td colspan="7" class="empty-state">{{ 'purchaseOrders.noResults' | transloco }}</td>
                </tr>
              </ng-template>
            </p-table>
          </div>

          <p-paginator
            [first]="first"
            [rows]="pagination.pageSize"
            [totalRecords]="pagination.totalCount"
            [rowsPerPageOptions]="[20, 50, 100]"
            (onPageChange)="onPageChange($event)"
          />
        </section>
      }
    </section>

    @if (showBuilderOverlay()) {
      <app-purchase-order-builder-page
        [purchaseOrderId]="editingPoId()"
        (closeRequested)="closeBuilder()"
      />
    }

    @if (receivingOrder; as order) {
      <app-receive-purchase-order-dialog
        [order]="order"
        [visible]="showReceiveDialog()"
        (visibleChange)="onReceiveDialogVisibleChange($event)"
        [submitting]="facade.isSubmitting()"
        (receive)="receiveOrder(order.purchaseOrderId, $event)"
        (closed)="closeReceiveDialog()"
      />
    }
  `,
  styles: [`
    :host { display: block; }
    .po-page {
      min-height: 100%;
      padding: .9rem 1rem 1rem;
      display: flex;
      flex-direction: column;
      gap: .75rem;
      background:
        radial-gradient(circle at top left, rgba(255, 224, 188, 0.55), transparent 32%),
        radial-gradient(circle at right 12rem top 10rem, rgba(209, 184, 159, 0.28), transparent 26%),
        linear-gradient(180deg, #fbf5ec 0%, #f7efe4 100%);
    }
    .po-hero {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 1rem;
      padding: .85rem 1rem;
      border: 1px solid #ead7c1;
      border-radius: 1.25rem;
      background:
        linear-gradient(135deg, rgba(255, 250, 243, 0.96), rgba(255, 238, 221, 0.92)),
        linear-gradient(135deg, rgba(255, 219, 183, 0.7), rgba(255, 251, 246, 0.08));
      box-shadow: 0 12px 32px rgba(87, 54, 20, 0.07);
    }
    .po-hero__copy { min-width: 0; }
    .eyebrow,
    .directory-panel__eyebrow {
      margin: 0;
      color: #9b5d20;
      font-size: .68rem;
      font-weight: 800;
      letter-spacing: .16em;
      text-transform: uppercase;
    }
    h1 {
      margin: .18rem 0 0;
      color: #2a1b12;
      font-family: Georgia, 'Times New Roman', serif;
      font-size: clamp(1.85rem, 3vw, 3.1rem);
      font-weight: 700;
      line-height: .98;
    }
    .subtitle,
    .directory-panel__subtitle {
      margin: .35rem 0 0;
      color: #6f5a48;
      font-size: .92rem;
      line-height: 1.35;
      max-width: 52rem;
    }
    .po-hero__meta {
      display: flex;
      flex-wrap: wrap;
      gap: .4rem;
      margin-top: .6rem;
    }
    .meta-chip {
      display: inline-flex;
      align-items: center;
      min-height: 1.9rem;
      padding: 0 .7rem;
      border: 1px solid #e7d6c3;
      border-radius: 999px;
      background: rgba(255, 252, 248, .9);
      color: #5f4634;
      font-size: .78rem;
      font-weight: 700;
    }
    .meta-chip--primary {
      border-color: #d58a4d;
      background: #fff0df;
      color: #8b4510;
    }
    .summary-grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: .65rem;
    }
    .summary-card {
      display: flex;
      flex-direction: column;
      gap: .35rem;
      min-height: 5.9rem;
      padding: .75rem .85rem;
      border: 1px solid #eadccf;
      border-radius: 1rem;
      background: linear-gradient(180deg, rgba(255, 251, 247, .96), rgba(255, 245, 235, .98));
      box-shadow: 0 8px 22px rgba(85, 49, 18, .07);
    }
    .summary-card__label {
      color: #7a6250;
      font-size: .66rem;
      font-weight: 800;
      letter-spacing: .14em;
      text-transform: uppercase;
    }
    .summary-card__value {
      color: #271911;
      font-family: Georgia, 'Times New Roman', serif;
      font-size: clamp(1.55rem, 1.8vw, 2.05rem);
      font-weight: 700;
      line-height: 1;
    }
    .summary-card--amber { background: linear-gradient(180deg, #fff7ed, #fff1dc); }
    .summary-card--sage { background: linear-gradient(180deg, #f5fbf4, #edf9ea); }
    .summary-card--terracotta { background: linear-gradient(180deg, #fff5ef, #ffe8dc); }
    .summary-card--ink { background: linear-gradient(180deg, #f9f8ff, #f1effd); }
    .directory-panel {
      display: flex;
      flex-direction: column;
      gap: .65rem;
      padding: .75rem;
      border: 1px solid #ead7c1;
      border-radius: 1.2rem;
      background: rgba(255, 251, 246, .92);
      box-shadow: 0 10px 30px rgba(78, 49, 20, .07);
    }
    .directory-panel__header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: .75rem;
      flex-wrap: wrap;
    }
    .directory-panel__header h2 {
      margin: .2rem 0 0;
      color: #271911;
      font-size: 1.2rem;
      font-weight: 800;
    }
    .directory-panel__metrics {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: .45rem;
    }
    .metric-chip {
      display: inline-flex;
      flex-direction: column;
      gap: .12rem;
      min-width: 8rem;
      padding: .45rem .65rem;
      border: 1px solid #ead7c1;
      border-radius: .75rem;
      background: linear-gradient(180deg, #fffaf3, #fff2e5);
    }
    .metric-chip strong {
      color: #2a1b12;
      font-size: .92rem;
      font-weight: 800;
      line-height: 1;
    }
    .metric-chip span {
      color: #8b6e57;
      font-size: .72rem;
      font-weight: 700;
    }
    .directory-panel__surface {
      overflow: hidden;
      border: 1px solid #eedcc8;
      border-radius: .9rem;
      background: #fffaf4;
    }
    .po-list-header__new-button { white-space: nowrap; }
    .po-filter-bar {
      display: grid;
      grid-template-columns: minmax(18rem, 1fr) 10rem 13rem 13rem auto;
      align-items: center;
      gap: .55rem;
    }
    .po-filter-bar__search {
      width: 100%;
      min-height: 2.35rem;
      border: 1px solid #ead7c1;
      border-radius: .75rem;
      background: #fffaf3;
      color: #312215;
      box-shadow: 0 1px 2px rgba(85, 49, 18, .06);
    }
    .po-filter-bar__search:enabled:focus {
      border-color: #ea580c;
      box-shadow: 0 0 0 3px rgba(234, 88, 12, .16);
    }
    .po-filter-bar__date { width: 100%; }
    .po-table-row { cursor: pointer; }
    .po-number,
    .money { font-weight: 800; color: #2a1b12; }
    .po-status-pill {
      display: inline-flex;
      align-items: center;
      min-height: 1.55rem;
      padding: 0 .6rem;
      border-radius: .65rem;
      font-size: .66rem;
      font-weight: 800;
      letter-spacing: .1em;
      text-transform: uppercase;
    }
    .po-status-pill--draft { background: #eff6ff; color: #1d4ed8; }
    .po-status-pill--placed { background: #e0f2fe; color: #0369a1; }
    .po-status-pill--partial { background: #fff7ed; color: #c2410c; }
    .po-status-pill--received { background: #dcfce7; color: #15803d; }
    .po-status-pill--closed { background: #ede9fe; color: #6d28d9; }
    .po-status-pill--cancelled { background: #fee2e2; color: #b91c1c; }
    .po-row-actions { display: flex; align-items: center; justify-content: flex-end; gap: .35rem; flex-wrap: wrap; }
    .empty-state { padding: 1.25rem; text-align: center; color: #8b6e57; }
    .loading { display: flex; justify-content: center; padding: 1.5rem; }
    :host ::ng-deep .po-filter-bar__select,
    :host ::ng-deep .po-filter-bar__date .p-datepicker-input,
    :host ::ng-deep .po-filter-bar__date-input {
      width: 100%;
      min-height: 2.35rem;
      border: 1px solid #ead7c1;
      border-radius: .75rem;
      background: #fffaf3;
      color: #312215;
      box-shadow: 0 1px 2px rgba(85, 49, 18, .06);
    }
    :host ::ng-deep .directory-panel__surface .p-datatable-table {
      border-collapse: collapse;
    }
    :host ::ng-deep .directory-panel__surface .p-datatable-thead > tr > th {
      border-color: #ead7c1;
      background: #fff5e7;
      color: #7a6250;
      font-size: .68rem;
      font-weight: 800;
      letter-spacing: .14em;
      text-transform: uppercase;
      padding: .55rem .8rem;
    }
    :host ::ng-deep .directory-panel__surface .p-datatable-tbody > tr > td {
      border-color: #ead7c1;
      color: #34281f;
      padding: .55rem .8rem;
    }
    :host ::ng-deep .p-paginator {
      border: 0;
      background: transparent;
      padding: .35rem 0 0;
      min-height: 2.5rem;
    }
    :host ::ng-deep .po-row-actions .p-button {
      min-height: 2.1rem;
      padding: .35rem .6rem;
    }
    @media (max-width: 1100px) {
      .summary-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .po-filter-bar { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (max-width: 720px) {
      .po-page { padding: .65rem; }
      .po-hero { flex-direction: column; border-radius: 1rem; }
      .summary-grid,
      .po-filter-bar { grid-template-columns: 1fr; }
      .directory-panel { border-radius: 1rem; }
    }
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
  protected readonly showReceiveDialog = signal(false);
  protected readonly receivingPoId = signal<string | null>(null);
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

  protected get receivingOrder(): PurchaseOrderDetail | null {
    const order = this.facade.selectedOrder();
    return order?.purchaseOrderId === this.receivingPoId() ? order : null;
  }

  protected summaryCards(): Array<{ labelKey: string; value: number; variant: 'count' | 'money' | 'progress'; tone: string }> {
    return [
      { labelKey: 'purchaseOrders.summary.totalOrders', value: this.pagination.totalCount, variant: 'count', tone: 'amber' },
      { labelKey: 'purchaseOrders.summary.pendingReceipt', value: this.pendingReceiptCount(), variant: 'count', tone: 'sage' },
      { labelKey: 'purchaseOrders.summary.receivedProgress', value: this.totalReceivedQuantity(), variant: 'progress', tone: 'terracotta' },
      { labelKey: 'purchaseOrders.summary.expectedTotal', value: this.totalExpectedValue(), variant: 'money', tone: 'ink' },
    ];
  }

  protected directoryMetrics(): Array<{ labelKey: string; value: number; variant: 'count' | 'money' }> {
    return [
      { labelKey: 'purchaseOrders.summary.expectedTotal', value: this.totalExpectedValue(), variant: 'money' },
      { labelKey: 'purchaseOrders.summary.pendingReceipt', value: this.pendingReceiptCount(), variant: 'count' },
      { labelKey: 'purchaseOrders.summary.filteredRows', value: this.facade.orders().length, variant: 'count' },
    ];
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

  protected openReceiveOrder(purchaseOrderId: string, event: Event): void {
    event.stopPropagation();
    this.receivingPoId.set(purchaseOrderId);
    this.showReceiveDialog.set(true);
    this.facade.loadDetail(purchaseOrderId);
  }

  protected onReceiveDialogVisibleChange(visible: boolean): void {
    this.showReceiveDialog.set(visible);
    if (!visible) {
      this.closeReceiveDialog();
    }
  }

  protected receiveOrder(purchaseOrderId: string, payload: ReceivePurchaseOrderRequest): void {
    this.facade.receiveOrder(purchaseOrderId, payload);
    this.showReceiveDialog.set(false);
  }

  protected closeReceiveDialog(): void {
    this.showReceiveDialog.set(false);
    this.receivingPoId.set(null);
    this.facade.loadOrders(this.facade.filters());
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

  protected pendingReceiptCount(): number {
    return this.facade.orders().filter((order) => order.status === 'Placed' || order.status === 'PartiallyReceived').length;
  }

  protected totalExpectedValue(): number {
    return this.facade.orders().reduce((total, order) => total + order.expectedTotal, 0);
  }

  protected totalExpectedQuantity(): number {
    return this.facade.orders().reduce((total, order) => total + order.expectedQuantity, 0);
  }

  protected totalReceivedQuantity(): number {
    return this.facade.orders().reduce((total, order) => total + order.receivedQuantity, 0);
  }

  protected statusTone(status: PurchaseOrderStatus): string {
    if (status === 'PartiallyReceived') return 'partial';
    return status.toLowerCase();
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
