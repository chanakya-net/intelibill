import { CommonModule } from '@angular/common';
import { Component, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';

import { DiscountRuleEditorDialogComponent } from '../components/discount-rule-editor-dialog.component';
import {
  DiscountRuleDto,
  DiscountRuleListItemDto,
  DiscountRuleType,
  DiscountService,
  GetDiscountRulesParams,
} from '../services/discount.service';

type DiscountStatusFilter = 'active' | 'disabled' | 'expired' | 'all';
type DiscountSortOption = 'created_desc' | 'created_asc' | 'name_asc' | 'name_desc';

interface SelectOption<T> {
  readonly label: string;
  readonly value: T;
}

@Component({
  selector: 'app-discounts-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslocoPipe,
    ButtonModule,
    CardModule,
    DialogModule,
    InputTextModule,
    ProgressSpinnerModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    DiscountRuleEditorDialogComponent,
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

  readonly statusOptions: SelectOption<DiscountStatusFilter>[] = [
    { label: 'discounts.filters.status.active', value: 'active' },
    { label: 'discounts.filters.status.disabled', value: 'disabled' },
    { label: 'discounts.filters.status.expired', value: 'expired' },
    { label: 'discounts.filters.status.all', value: 'all' },
  ];

  readonly ruleTypeOptions: SelectOption<DiscountRuleType | ''>[] = [
    { label: 'discounts.filters.type.all', value: '' },
    { label: 'discounts.ruleType.BatchPercentage', value: 'BatchPercentage' },
    { label: 'discounts.ruleType.SalePercentage', value: 'SalePercentage' },
    { label: 'discounts.ruleType.SaleThresholdPercentage', value: 'SaleThresholdPercentage' },
  ];

  readonly sortOptions: SelectOption<DiscountSortOption>[] = [
    { label: 'discounts.filters.sort.createdDesc', value: 'created_desc' },
    { label: 'discounts.filters.sort.createdAsc', value: 'created_asc' },
    { label: 'discounts.filters.sort.nameAsc', value: 'name_asc' },
    { label: 'discounts.filters.sort.nameDesc', value: 'name_desc' },
  ];
  readonly sortValue = signal<DiscountSortOption>('created_desc');

  readonly selectedRuleTitle = computed(() => this.selectedRule()?.name ?? '');
  readonly selectedRuleIsExpired = computed(() => this.isExpired(this.selectedRule()?.endsAt ?? null));
  readonly selectedRuleStatusKey = computed(() => this.getRuleStatusKey(this.selectedRule()));

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

  onResetFilters(): void {
    this.statusFilter.set('active');
    this.ruleTypeFilter.set('');
    this.searchValue.set('');
    this.sortValue.set('created_desc');
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
  }

  onConfirmDisable(): void {
    const id = this.selectedRuleId();
    if (!id) return;

    this.disableSubmitting.set(true);
    const reason = this.disableReason().trim();

    this.discountService.disableDiscountRule(id, reason ? reason : null).subscribe({
      next: (rule) => {
        this.selectedRule.set(rule);
        this.disableSubmitting.set(false);
        this.showDisableDialog.set(false);
        this.refreshList();
      },
      error: () => {
        this.disableSubmitting.set(false);
        this.detailError.set('discounts.errors.disableFailed');
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

  private refreshList(): void {
    const params: GetDiscountRulesParams = {
      status: this.statusFilter(),
      ruleType: this.ruleTypeFilter() || undefined,
      search: this.searchValue().trim() || undefined,
      sort: this.sortValue() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    };
    this.loadList(params);
  }

  private loadList(params: GetDiscountRulesParams): void {
    this.listLoading.set(true);
    this.listError.set('');

    this.discountService.getDiscountRules(params).subscribe({
      next: (result) => {
        this.listItems.set(result.items);
        this.totalCount.set(result.totalCount);
        this.pageNumber.set(result.pageNumber);
        this.pageSize.set(result.pageSize);
        this.listLoading.set(false);

        const selectedId = this.selectedRuleId();
        if (!selectedId && result.items.length > 0) {
          this.selectedRuleId.set(result.items[0].id);
        }
      },
      error: () => {
        this.listLoading.set(false);
        this.listError.set('discounts.errors.listFailed');
      },
    });
  }

  private loadDetail(id: string): void {
    this.detailLoading.set(true);
    this.detailError.set('');

    this.discountService.getDiscountRule(id).subscribe({
      next: (rule) => {
        this.selectedRule.set(rule);
        this.detailLoading.set(false);
      },
      error: () => {
        this.detailLoading.set(false);
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
