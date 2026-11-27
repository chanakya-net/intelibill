import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import { DiscountRuleListItemDto } from '../services/discount.service';

@Component({
  selector: 'app-discounts-table',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    ProgressSpinnerModule,
    TableModule,
    TagModule,
    TranslocoPipe,
  ],
  templateUrl: './discounts-table.component.html',
  styleUrl: './discounts-table.component.scss',
})
export class DiscountsTableComponent {
  @Input({ required: true }) listItems: readonly DiscountRuleListItemDto[] = [];
  @Input() listLoading = false;
  @Input() selectedRuleId: string | null = null;
  @Input() totalCount = 0;
  @Input() pageNumber = 1;
  @Input() pageSize = 20;
  @Input({ required: true }) statusKey!: (item: DiscountRuleListItemDto) => string;
  @Input({ required: true }) statusSeverity!: (
    item: DiscountRuleListItemDto,
  ) => 'success' | 'secondary' | 'danger';
  @Input({ required: true }) formatDate!: (value: string | null) => string;

  @Output() selectRule = new EventEmitter<string>();
  @Output() pageChange = new EventEmitter<number>();

  onSelectRule(ruleId: string): void {
    this.selectRule.emit(ruleId);
  }

  onPreviousPage(): void {
    if (this.pageNumber <= 1 || this.listLoading) return;
    this.pageChange.emit(this.pageNumber - 1);
  }

  onNextPage(): void {
    if (this.pageNumber * this.pageSize >= this.totalCount || this.listLoading) return;
    this.pageChange.emit(this.pageNumber + 1);
  }
}
