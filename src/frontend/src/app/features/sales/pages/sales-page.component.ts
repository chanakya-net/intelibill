import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { DialogModule } from 'primeng/dialog';
import { SkeletonModule } from 'primeng/skeleton';
import { DatePickerModule } from 'primeng/datepicker';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';

import type { SaleListItemDto } from '../services/sale.models';
import type { SaleHistoryStatus, SalesHistoryQueryParams } from '../services/sale.models';
import { getPaymentMethodLabel, getPaymentMethodSeverity } from '../services/sale.models';
import { SalesFacade } from '../state/sales.facade';
import { SaleDetailOverlayComponent } from '../components/sale-detail-overlay.component';
import { SalesExportToolbarComponent } from '../components/sales-export-toolbar.component';
import { OfflineSalesQueueSyncService } from '../services/offline-sales-queue-sync.service';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';

@Component({
  selector: 'app-sales-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    TagModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    TableModule,
    DialogModule,
    SkeletonModule,
    DatePickerModule,
    SelectModule,
    SelectButtonModule,
    SaleDetailOverlayComponent,
    SalesExportToolbarComponent,
    TranslocoPipe,
  ],
  templateUrl: './sales-page.component.html',
  styleUrl: './sales-page.component.scss',
})
export class SalesPageComponent {
  private readonly salesFacade = inject(SalesFacade);
  private readonly offlineSalesQueueSync = inject(OfflineSalesQueueSyncService);

  readonly sales = this.salesFacade.allSales;
  readonly salesPagination = this.salesFacade.salesPagination;
  readonly salesHistorySummary = this.salesFacade.salesHistorySummary;
  readonly offlineQueueCounts = this.offlineSalesQueueSync.visibleCounts;
  readonly hasOfflineQueueStatus = computed(() => this.offlineQueueCounts().totalVisible > 0);
  readonly isRetryingOfflineQueue = signal(false);
  readonly tableSales = computed(() => [...this.sales()]);
  readonly isLoading = this.salesFacade.loadingSales;
  readonly serverError = this.salesFacade.errorMessage;
  readonly showDetailOverlay = signal(false);
  readonly viewingSaleId = signal<string | null>(null);

  readonly fromDate = signal<Date>(this.getDefaultFromDate());
  readonly toDate = signal<Date>(this.getDefaultToDate());
  readonly statusFilter = signal<SaleHistoryStatus | 'all'>('all');
  readonly searchValue = signal('');
  readonly reportLevel = signal<'summary' | 'lineItems'>('summary');
  readonly pageNumber = signal(this.salesFacade.salesPagination().pageNumber || 1);
  readonly pageSize = signal(Math.max(this.salesFacade.salesPagination().pageSize, 20));

  readonly pageSizeOptions = [
    { label: '10', value: 10 },
    { label: '20', value: 20 },
    { label: '50', value: 50 },
  ];

  readonly reportLevelOptions = [
    { label: 'sales.history.reportLevel.summary', value: 'summary' as const },
    { label: 'sales.history.reportLevel.lineItems', value: 'lineItems' as const },
  ];

  readonly statusFilterOptions = [
    { label: 'sales.history.status.all', value: 'all' as const },
    { label: 'sales.history.status.paid', value: 'paid' as const },
    { label: 'sales.history.status.refunded', value: 'refunded' as const },
    { label: 'sales.history.status.unknown', value: 'unknown' as const },
  ];

  private readonly debouncedSearch = signal('');
  private readonly lastAppliedFilterKey = signal<string>('');

  readonly totalPages = computed(() => {
    const total = this.salesPagination().totalCount;
    const size = Math.max(1, this.pageSize());
    return Math.max(1, Math.ceil(total / size));
  });

  readonly rangeStart = computed(() => {
    const { totalCount, pageNumber, pageSize } = this.salesPagination();
    if (totalCount === 0) return 0;
    return (pageNumber - 1) * pageSize + 1;
  });

  readonly rangeEnd = computed(() => {
    const { totalCount, pageNumber, pageSize } = this.salesPagination();
    if (totalCount === 0) return 0;
    return Math.min(totalCount, pageNumber * pageSize);
  });

  readonly paginationItems = computed(() => this.getPaginationItems(this.totalPages(), this.pageNumber()));

  constructor() {
    effect((onCleanup) => {
      const search = this.searchValue().trim();
      const handle = setTimeout(() => this.debouncedSearch.set(search), 300);
      onCleanup(() => clearTimeout(handle));
    });

    effect(() => {
      const { pageNumber: storePageNumber, pageSize: storePageSize } = this.salesPagination();
      if (storePageNumber && storePageNumber !== this.pageNumber()) {
        untracked(() => this.pageNumber.set(storePageNumber));
      }
      if (storePageSize > 0 && storePageSize !== this.pageSize()) {
        untracked(() => this.pageSize.set(storePageSize));
      }
    });

    effect(() => {
      const filterKey = this.getFilterKey();
      if (filterKey === this.lastAppliedFilterKey()) {
        return;
      }

      this.lastAppliedFilterKey.set(filterKey);
      if (this.pageNumber() !== 1) {
        this.pageNumber.set(1);
      }
    });

    effect(() => {
      const params = this.buildQueryParams();
      this.salesFacade.loadSales(params);
    });

    void this.offlineSalesQueueSync.cleanupSyncedRecords()
      .then(() => this.offlineSalesQueueSync.refreshActiveStatusCounts());
  }

  paymentMethodLabel(method: number): string {
    return getPaymentMethodLabel(method);
  }

  paymentMethodSeverity(method: number): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    return getPaymentMethodSeverity(method);
  }

  statusLabelKey(status: SaleHistoryStatus): string {
    switch (status) {
      case 'paid':
        return 'sales.history.status.paid';
      case 'partiallyPaid':
        return 'sales.history.status.partiallyPaid';
      case 'refunded':
      case 'returned':
        return 'sales.history.status.refunded';
      case 'unknown':
      default:
        return 'sales.history.status.unknown';
    }
  }

  statusSeverity(status: SaleHistoryStatus): 'success' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case 'paid':
        return 'success';
      case 'partiallyPaid':
        return 'warn';
      case 'refunded':
      case 'returned':
        return 'danger';
      case 'unknown':
      default:
        return 'secondary';
    }
  }

  onPageChange(nextPage: number): void {
    const clamped = Math.min(Math.max(1, nextPage), this.totalPages());
    this.pageNumber.set(clamped);
  }

  clearFilters(): void {
    this.fromDate.set(this.getDefaultFromDate());
    this.toDate.set(this.getDefaultToDate());
    this.statusFilter.set('all');
    this.searchValue.set('');
    this.debouncedSearch.set('');
    this.reportLevel.set('summary');
    this.pageNumber.set(1);
    this.pageSize.set(20);
  }

  onViewSale(sale: SaleListItemDto): void {
    this.viewingSaleId.set(sale.saleId);
    this.salesFacade.loadSaleDetail(sale.saleId);
    this.showDetailOverlay.set(true);
  }

  onCloseDetail(): void {
    this.showDetailOverlay.set(false);
    this.viewingSaleId.set(null);
    this.salesFacade.clearSaleDetail();
  }

  onRetryOfflineQueue(): void {
    if (this.isRetryingOfflineQueue()) return;

    this.isRetryingOfflineQueue.set(true);
    void this.offlineSalesQueueSync.retryActiveShop()
      .finally(() => this.isRetryingOfflineQueue.set(false));
  }

  private buildQueryParams(): SalesHistoryQueryParams {
    const status = this.statusFilter();
    return {
      from: formatLocalIsoDate(this.fromDate()),
      to: formatLocalIsoDate(this.toDate()),
      search: this.debouncedSearch() || undefined,
      status: status === 'all' ? undefined : status,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    };
  }

  private getFilterKey(): string {
    const status = this.statusFilter();
    return [
      formatLocalIsoDate(this.fromDate()),
      formatLocalIsoDate(this.toDate()),
      status === 'all' ? 'all' : status,
      this.debouncedSearch(),
      this.pageSize(),
    ].join('|');
  }

  private getDefaultFromDate(): Date {
    const date = new Date();
    date.setDate(date.getDate() - 30);
    date.setHours(0, 0, 0, 0);
    return date;
  }

  private getDefaultToDate(): Date {
    const date = new Date();
    date.setHours(23, 59, 59, 999);
    return date;
  }

  private getPaginationItems(totalPages: number, currentPage: number): ReadonlyArray<number | 'ellipsis'> {
    if (totalPages <= 7) {
      return Array.from({ length: totalPages }, (_, i) => i + 1);
    }

    const items: Array<number | 'ellipsis'> = [];
    const pushUnique = (value: number | 'ellipsis') => {
      if (items.length === 0 || items[items.length - 1] !== value) {
        items.push(value);
      }
    };

    pushUnique(1);

    const start = Math.max(2, currentPage - 1);
    const end = Math.min(totalPages - 1, currentPage + 1);

    if (start > 2) {
      pushUnique('ellipsis');
    }

    for (let p = start; p <= end; p++) {
      pushUnique(p);
    }

    if (end < totalPages - 1) {
      pushUnique('ellipsis');
    }

    pushUnique(totalPages);
    return items;
  }
}
