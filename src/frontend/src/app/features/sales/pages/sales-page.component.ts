import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
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
import { DialogModule } from 'primeng/dialog';

import { SaleListItemDto } from '../services/sale.service';
import { SalesFacade } from '../state/sales.facade';
import { SaleDetailOverlayComponent } from '../components/sale-detail-overlay.component';

@Component({
  selector: 'app-sales-page',
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
    DialogModule,
    SaleDetailOverlayComponent,
    TranslocoPipe,
  ],
  templateUrl: './sales-page.component.html',
  styleUrl: './sales-page.component.scss',
})
export class SalesPageComponent {
  private readonly salesFacade = inject(SalesFacade);

  readonly sales = this.salesFacade.allSales;
  readonly tableSales = computed(() => [...this.sales()]);
  readonly searchValue = signal('');
  readonly filteredSales = computed(() => {
    const q = this.searchValue().toLowerCase();
    if (!q) return [...this.sales()];
    return this.sales().filter(
      (s) =>
        s.invoiceNumber.toLowerCase().includes(q) ||
        (s.customerName ?? '').toLowerCase().includes(q) ||
        (s.customerPhone ?? '').toLowerCase().includes(q),
    );
  });
  readonly isLoading = this.salesFacade.loadingSales;
  readonly serverError = this.salesFacade.errorMessage;
  readonly showDetailOverlay = signal(false);
  readonly viewingSaleId = signal<string | null>(null);

  constructor() {
    this.salesFacade.loadSales();
  }

  paymentMethodLabel(method: number): string {
    switch (method) {
      case 1: return 'Cash';
      case 2: return 'UPI';
      case 3: return 'Card';
      case 4: return 'Credit';
      default: return 'Unknown';
    }
  }

  paymentMethodSeverity(method: number): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (method) {
      case 1: return 'success';
      case 2: return 'info';
      case 3: return 'warn';
      case 4: return 'danger';
      default: return 'secondary';
    }
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
}
