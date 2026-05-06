import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';

import { SalesFacade } from '../../state/sales.facade';
import { ProfitLossReportItemDto, ProfitLossReportRowType } from '../../services/sale.service';
import { TableFilterBarComponent } from '../../../../shared/components/table-filter-bar/table-filter-bar.component';

@Component({
  selector: 'app-profit-loss-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CardModule,
    TagModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    ProgressSpinnerModule,
    TableModule,
    TableFilterBarComponent,
    TranslocoPipe,
  ],
  templateUrl: './profit-loss-page.component.html',
  styleUrl: './profit-loss-page.component.scss',
})
export class ProfitLossPageComponent {
  private readonly salesFacade = inject(SalesFacade);

  readonly report = this.salesFacade.profitLossReport;
  readonly tableData = computed(() => [...this.report()]);
  readonly searchValue = signal('');
  readonly filteredReport = computed(() => {
    const q = this.searchValue().toLowerCase();
    if (!q) return [...this.report()];
    return this.report().filter(
      (s) =>
        s.referenceNumber.toLowerCase().includes(q) ||
        s.rowType.toLowerCase().includes(q) ||
        (s.partyName ?? '').toLowerCase().includes(q)
    );
  });
  readonly isLoading = this.salesFacade.loadingProfitLossReport;
  readonly serverError = this.salesFacade.errorMessage;

  constructor() {
    this.salesFacade.loadProfitLossReport();
  }

  getProfitSeverity(amount: number): 'success' | 'danger' {
    return amount >= 0 ? 'success' : 'danger';
  }

  getRowTypeTranslationKey(rowType: ProfitLossReportRowType): string {
    return `sales.profitLoss.rowTypes.${rowType}`;
  }

  getPartyFallbackTranslationKey(item: ProfitLossReportItemDto): string {
    return item.rowType === 'InventoryAdjustment'
      ? 'sales.profitLoss.adjustmentParty'
      : 'sales.history.walkIn';
  }
}
