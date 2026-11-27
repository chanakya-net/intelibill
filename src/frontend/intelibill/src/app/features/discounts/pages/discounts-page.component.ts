import { CommonModule } from '@angular/common';
import { Component, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SkeletonModule } from 'primeng/skeleton';
import { TagModule } from 'primeng/tag';

import {
  DiscountStatusFilter,
  DiscountSortOption,
  DiscountsFilterBarComponent,
} from '../components/discounts-filter-bar.component';
import { DiscountRuleEditorDialogComponent } from '../components/discount-rule-editor-dialog.component';
import { DiscountsTableComponent } from '../components/discounts-table.component';
import { DiscountsDisableDialogComponent } from '../components/discounts-disable-dialog.component';
import {
  DiscountRuleDto,
  DiscountRuleListItemDto,
  DiscountRuleType,
  DiscountService,
  GetDiscountRulesParams,
} from '../services/discount.service';

type DiscountSummaryCard = {
  labelKey: string;
  value: number;
  tone: 'amber' | 'sage' | 'terracotta' | 'ink';
};

type DiscountDirectoryMetric = {
  labelKey: string;
  value: number;
};

@Component({
  selector: 'app-discounts-page',
  standalone: true,
  imports: [
    CommonModule,
    TranslocoPipe,
    ButtonModule,
    ProgressSpinnerModule,
    SkeletonModule,
    TagModule,
    DiscountsFilterBarComponent,
    DiscountsTableComponent,
    DiscountRuleEditorDialogComponent,
    DiscountsDisableDialogComponent,
  ],
  templateUrl: './discounts-page.component.html',
  styleUrl: './discounts-page.component.scss',
})
export class DiscountsPageComponent {
  private readonly discountService = inject(DiscountService);
  @ViewChild(DiscountRuleEditorDialogComponent)
  private editorDialog?: DiscountRuleEditorDialogComponent;

  readonly statusFilter = signal<DiscountStatusFilter>('active');
  readonly ruleTypeFilter = signal<DiscountRuleType | ''>('');
  readonly searchValue = signal('');
  readonly sortValue = signal<DiscountSortOption>('created_desc');

  readonly listItems = signal<readonly DiscountRuleListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly listLoading = signal(false);
  readonly listError = signal('');

  readonly selectedRuleId = signal<string | null>(null);
  readonly selectedRule = signal<DiscountRuleDto | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal('');

  readonly showDisableDialog = signal(false);
  readonly disableReason = signal('');
  readonly disableSubmitting = signal(false);
  readonly disableError = signal('');

  private listRequestId = 0;
  private detailRequestId = 0;

  readonly selectedRuleTitle = computed(() => this.selectedRule()?.name ?? '');
  readonly selectedRuleStatusKey = computed(() => this.getRuleStatusKey(this.selectedRule()));

  readonly activeOnPageCount = computed(
    () => this.listItems().filter((item) => item.isActive && !this.isExpired(item.endsAt)).length,
  );
  readonly disabledOnPageCount = computed(
    () => this.listItems().filter((item) => !item.isActive).length,
  );
  readonly expiredOnPageCount = computed(
    () => this.listItems().filter((item) => item.isActive && this.isExpired(item.endsAt)).length,
  );

  readonly summaryCards = computed<DiscountSummaryCard[]>(() => [
    {
      labelKey: 'discounts.summary.totalRules',
      value: this.totalCount(),
      tone: 'amber',
    },
    {
      labelKey: 'discounts.summary.activeOnPage',
      value: this.activeOnPageCount(),
      tone: 'sage',
    },
    {
      labelKey: 'discounts.summary.disabledOnPage',
      value: this.disabledOnPageCount(),
      tone: 'terracotta',
    },
    {
      labelKey: 'discounts.summary.expiredOnPage',
      value: this.expiredOnPageCount(),
      tone: 'ink',
    },
  ]);

  readonly directoryMetrics = computed<DiscountDirectoryMetric[]>(() => [
    {
      labelKey: 'discounts.summary.totalRules',
      value: this.totalCount(),
    },
    {
      labelKey: 'discounts.summary.activeOnPage',
      value: this.activeOnPageCount(),
    },
    {
      labelKey: 'discounts.summary.onThisPage',
      value: this.listItems().length,
    },
  ]);

  constructor() {
    effect(() => {
      const params: GetDiscountRulesParams = {
        status: this.statusFilter(),
        ruleType: this.ruleTypeFilter() || undefined,
        search: this.searchValue().trim() || undefined,
        sort: this.sortValue() || undefined,
        page: this.pageNumber(),
        pageSize: this.pageSize(),
      };
      this.loadList(params);
    });

    effect(() => {
      const id = this.selectedRuleId();
      if (!id) return;
      this.loadDetail(id);
    });
  }

  onSearchChange(value: string): void {
    this.searchValue.set(value);
    this.pageNumber.set(1);
  }

  onStatusFilterChange(value: DiscountStatusFilter): void {
    this.statusFilter.set(value);
    this.pageNumber.set(1);
  }

  onRuleTypeFilterChange(value: DiscountRuleType | ''): void {
    this.ruleTypeFilter.set(value);
    this.pageNumber.set(1);
  }

  onSortChange(value: DiscountSortOption): void {
    this.sortValue.set(value);
    this.pageNumber.set(1);
  }

  onSelectRule(id: string): void {
    if (this.selectedRuleId() === id) return;
    this.selectedRuleId.set(id);
  }

  onNavigateRule(id: string | null): void {
    if (!id) return;
    this.selectedRuleId.set(id);
  }

  onOpenDisableDialog(): void {
    const rule = this.selectedRule();
    if (!rule || !rule.isActive) return;
    this.disableReason.set('');
    this.disableError.set('');
    this.showDisableDialog.set(true);
  }

  onOpenCreateRule(): void {
    this.editorDialog?.open('create');
  }

  onOpenEditRule(): void {
    const rule = this.selectedRule();
    if (!rule || !rule.isActive) return;
    this.editorDialog?.open('edit', rule);
  }

  onRuleSaved(rule: DiscountRuleDto): void {
    this.selectedRuleId.set(rule.id);
    this.selectedRule.set(rule);
    this.refreshList();
  }

  onCloseDisableDialog(): void {
    if (this.disableSubmitting()) return;
    this.showDisableDialog.set(false);
    this.disableReason.set('');
    this.disableError.set('');
  }

  onConfirmDisable(): void {
    const id = this.selectedRuleId();
    if (!id || this.disableSubmitting()) return;

    this.disableSubmitting.set(true);
    this.disableError.set('');
    const reason = this.disableReason().trim();

    this.discountService.disableDiscountRule(id, reason ? reason : null).subscribe({
      next: (rule) => {
        this.selectedRule.set(rule);
        this.disableSubmitting.set(false);
        this.showDisableDialog.set(false);
        this.refreshList(true);
      },
      error: () => {
        this.disableSubmitting.set(false);
        this.disableError.set('discounts.errors.disableFailed');
      },
    });
  }

  formatDate(value: string | null): string {
    if (!value) return '-';
    try {
      return new Date(value).toLocaleString();
    } catch {
      return value;
    }
  }

  listStatusKey(item: DiscountRuleListItemDto): string {
    if (!item.isActive) return 'discounts.status.disabled';
    if (this.isExpired(item.endsAt)) return 'discounts.status.expired';
    return 'discounts.status.active';
  }

  listStatusSeverity(item: DiscountRuleListItemDto): 'success' | 'secondary' | 'danger' {
    if (!item.isActive) return 'secondary';
    if (this.isExpired(item.endsAt)) return 'danger';
    return 'success';
  }

  detailStatusSeverity(rule: DiscountRuleDto | null): 'success' | 'secondary' | 'danger' {
    if (!rule) return 'secondary';
    if (!rule.isActive) return 'secondary';
    if (this.isExpired(rule.endsAt)) return 'danger';
    return 'success';
  }

  private refreshList(retainSelectedDetail = false): void {
    const params: GetDiscountRulesParams = {
      status: this.statusFilter(),
      ruleType: this.ruleTypeFilter() || undefined,
      search: this.searchValue().trim() || undefined,
      sort: this.sortValue() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    };
    this.loadList(params, retainSelectedDetail);
  }

  private loadList(params: GetDiscountRulesParams, retainSelectedDetail = false): void {
    const requestId = ++this.listRequestId;
    this.listLoading.set(true);
    this.listError.set('');

    this.discountService.getDiscountRules(params).subscribe({
      next: (result) => {
        if (requestId !== this.listRequestId) return;
        this.listItems.set(result.items);
        this.totalCount.set(result.totalCount);
        this.pageNumber.set(result.pageNumber);
        this.pageSize.set(result.pageSize);
        this.listLoading.set(false);

        const selectedId = this.selectedRuleId();
        const selectionStillVisible = result.items.some((item) => item.id === selectedId);
        if (retainSelectedDetail && selectedId) {
          return;
        }

        const nextId = selectionStillVisible ? selectedId : (result.items[0]?.id ?? null);
        if (nextId === selectedId) return;

        this.detailRequestId += 1;
        this.detailLoading.set(false);
        this.selectedRule.set(null);
        this.detailError.set('');
        this.selectedRuleId.set(nextId);
      },
      error: () => {
        if (requestId !== this.listRequestId) return;
        this.listLoading.set(false);
        this.listError.set('discounts.errors.listFailed');
        this.listItems.set([]);
        this.totalCount.set(0);
        this.detailRequestId += 1;
        this.detailLoading.set(false);
        this.selectedRuleId.set(null);
        this.selectedRule.set(null);
      },
    });
  }

  private loadDetail(id: string): void {
    const requestId = ++this.detailRequestId;
    this.detailLoading.set(true);
    this.detailError.set('');

    this.discountService.getDiscountRule(id).subscribe({
      next: (rule) => {
        if (requestId !== this.detailRequestId || this.selectedRuleId() !== id) return;
        this.selectedRule.set(rule);
        this.detailLoading.set(false);
      },
      error: () => {
        if (requestId !== this.detailRequestId || this.selectedRuleId() !== id) return;
        this.detailLoading.set(false);
        this.selectedRule.set(null);
        this.detailError.set('discounts.errors.detailFailed');
      },
    });
  }

  private isExpired(endsAt: string | null): boolean {
    if (!endsAt) return false;
    const endMs = Date.parse(endsAt);
    if (!Number.isFinite(endMs)) return false;
    return endMs < Date.now();
  }

  private getRuleStatusKey(rule: DiscountRuleDto | null): string {
    if (!rule) return 'discounts.status.unknown';
    if (!rule.isActive) return 'discounts.status.disabled';
    if (this.isExpired(rule.endsAt)) return 'discounts.status.expired';
    return 'discounts.status.active';
  }
}
