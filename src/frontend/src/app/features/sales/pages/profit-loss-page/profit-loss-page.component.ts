import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SkeletonModule } from 'primeng/skeleton';
import { DatePickerModule } from 'primeng/datepicker';
import { SelectModule } from 'primeng/select';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { SalesFacade } from '../../state/sales.facade';
import type {
  ProfitLossReportItemDto,
  ProfitLossReportQueryParams,
  ProfitLossReportRowType,
  ProfitLossReportTypeFilter,
} from '../../services/sale.models';
import { formatLocalIsoDate } from '../../../../shared/utils/date-time.util';

@Component({
  selector: 'app-profit-loss-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    TagModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SkeletonModule,
    DatePickerModule,
    SelectModule,
    ProgressSpinnerModule,
    TableModule,
    TranslocoPipe,
  ],
  templateUrl: './profit-loss-page.component.html',
  styleUrl: './profit-loss-page.component.scss',
})
export class ProfitLossPageComponent {
  private readonly salesFacade = inject(SalesFacade);

  readonly report = this.salesFacade.profitLossItems;
  readonly summary = this.salesFacade.profitLossSummary;
  readonly pagination = this.salesFacade.profitLossPagination;
  readonly tableData = computed(() => [...this.report()]);
  readonly searchValue = signal('');
  readonly fromDate = signal<Date>(this.getDefaultFromDate());
  readonly toDate = signal<Date>(this.getDefaultToDate());
  readonly typeFilter = signal<ProfitLossReportTypeFilter>('all');
  readonly pageNumber = signal(this.pagination().pageNumber || 1);
  readonly pageSize = signal(this.getDefaultPageSize(this.pagination().pageSize));
  readonly debouncedSearch = signal('');
  readonly isLoading = this.salesFacade.loadingProfitLossReport;
  readonly serverError = this.salesFacade.errorMessage;
  readonly loadingRows = [1, 2, 3, 4, 5];
  private readonly lastAppliedFilterKey = signal<string>('');

  readonly typeFilterOptions = [
    { label: 'sales.profitLoss.filters.type.all', value: 'all' as const },
    { label: 'sales.profitLoss.filters.type.sale', value: 'sale' as const },
    { label: 'sales.profitLoss.filters.type.saleReturn', value: 'saleReturn' as const },
    { label: 'sales.profitLoss.filters.type.inventoryAdjustment', value: 'inventoryAdjustment' as const },
  ];

  readonly pageSizeOptions = [
    { label: '10', value: 10 },
    { label: '20', value: 20 },
    { label: '50', value: 50 },
  ];

  readonly kpiCards = [
    {
      label: 'sales.profitLoss.kpi.netProfit',
      value: () => this.summary()?.netProfitAfterTax ?? 0,
      currency: true,
    },
    {
      label: 'sales.profitLoss.kpi.revenue',
      value: () => this.summary()?.revenueIncludingTax ?? 0,
      currency: true,
    },
    {
      label: 'sales.profitLoss.kpi.totalCost',
      value: () => this.summary()?.totalCost ?? 0,
      currency: true,
    },
    {
      label: 'sales.profitLoss.kpi.avgMargin',
      value: () => this.summary()?.averageMarginPercent ?? null,
      currency: false,
      percent: true,
    },
  ];

  readonly totalPages = computed(() => {
    const total = this.pagination().totalCount;
    const size = Math.max(1, this.pageSize());
    return Math.max(1, Math.ceil(total / size));
  });

  readonly rangeStart = computed(() => {
    const { totalCount, pageNumber, pageSize } = this.pagination();
    if (totalCount === 0) return 0;
    return (pageNumber - 1) * pageSize + 1;
  });

  readonly rangeEnd = computed(() => {
    const { totalCount, pageNumber, pageSize } = this.pagination();
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
      const { pageNumber: storePageNumber, pageSize: storePageSize } = this.pagination();
      const normalizedSize = this.getDefaultPageSize(storePageSize);
      if (normalizedSize !== this.pageSize()) {
        untracked(() => this.pageSize.set(normalizedSize));
      }
      if (storePageNumber && this.pageNumber() === 1 && storePageNumber !== 1) {
        untracked(() => this.pageNumber.set(storePageNumber));
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
      this.salesFacade.loadProfitLossReport(params);
    });
  }

  totalAmount(value: number): string {
    return `₹${value.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  formatMarginPercent(value: number | null): string {
    if (value === null) return '--';
    return `${value.toLocaleString('en-IN', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`;
  }

  getKpiValue(kpi: { value: () => number | null; currency?: boolean; percent?: boolean }): string {
    const value = kpi.value();
    if (value === null) return '--';
    if (kpi.percent) return this.formatMarginPercent(value);
    return this.totalAmount(value);
  }

  getProfitSeverity(amount: number): 'success' | 'danger' {
    return amount >= 0 ? 'success' : 'danger';
  }

  getTypeBadgeSeverity(
    rowType: ProfitLossReportItemDto['rowType'],
  ): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (rowType) {
      case 'Sale':
        return 'success';
      case 'SaleReturn':
        return 'warn';
      case 'InventoryAdjustment':
        return 'secondary';
      default:
        return 'info';
    }
  }

  getRowTypeTranslationKey(rowType: ProfitLossReportRowType): string {
    return `sales.profitLoss.rowTypes.${rowType}`;
  }

  getPartyFallbackTranslationKey(item: ProfitLossReportItemDto): string {
    return item.rowType === 'InventoryAdjustment'
      ? 'sales.profitLoss.adjustmentParty'
      : 'sales.history.walkIn';
  }

  onPageChange(nextPage: number): void {
    const clamped = Math.min(Math.max(1, nextPage), this.totalPages());
    this.pageNumber.set(clamped);
  }

  onPageSizeChange(nextPageSize: number): void {
    this.pageSize.set(this.getDefaultPageSize(nextPageSize));
  }

  clearFilters(): void {
    this.fromDate.set(this.getDefaultFromDate());
    this.toDate.set(this.getDefaultToDate());
    this.typeFilter.set('all');
    this.searchValue.set('');
    this.debouncedSearch.set('');
    this.pageNumber.set(1);
    this.pageSize.set(20);
  }

  onExport(): void {
    // Placeholder hook for future export implementation.
  }

  private buildQueryParams(): ProfitLossReportQueryParams {
    const type = this.typeFilter();
    const typeParam = type === 'all' ? undefined : type;
    return {
      from: formatLocalIsoDate(this.fromDate()),
      to: formatLocalIsoDate(this.toDate()),
      type: typeParam,
      search: this.debouncedSearch() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    };
  }

  private getFilterKey(): string {
    return [
      formatLocalIsoDate(this.fromDate()),
      formatLocalIsoDate(this.toDate()),
      this.typeFilter(),
      this.debouncedSearch(),
      this.pageSize(),
    ].join('|');
  }

  private getDefaultFromDate(): Date {
    const date = new Date();
    date.setDate(date.getDate() - 6);
    date.setHours(0, 0, 0, 0);
    return date;
  }

  private getDefaultToDate(): Date {
    const date = new Date();
    date.setHours(23, 59, 59, 999);
    return date;
  }

  private getDefaultPageSize(value: number): number {
    return Math.max(1, value || 20);
  }

  private getPaginationItems(totalPages: number, currentPage: number): ReadonlyArray<number | 'ellipsis'> {
    if (totalPages <= 7) {
      return Array.from({ length: totalPages }, (_, index) => index + 1);
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

    for (let p = start; p <= end; p += 1) {
      pushUnique(p);
    }

    if (end < totalPages - 1) {
      pushUnique('ellipsis');
    }

    pushUnique(totalPages);
    return items;
  }
}
