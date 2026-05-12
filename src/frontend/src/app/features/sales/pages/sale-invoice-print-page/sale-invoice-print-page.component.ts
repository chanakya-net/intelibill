import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../../../core/auth/auth.service';
import { ShopDetails, ShopService } from '../../../shops/services/shop.service';
import { SaleDto, SaleService } from '../../services/sale.service';
import { SaleInvoiceA4Component } from '../../components/sale-invoice-a4.component';
import { SaleInvoiceThermalComponent } from '../../components/sale-invoice-thermal.component';

type InvoiceTemplate = 'a4' | 'thermal';

@Component({
  selector: 'app-sale-invoice-print-page',
  standalone: true,
  imports: [CommonModule, TranslocoPipe, SaleInvoiceA4Component, SaleInvoiceThermalComponent],
  templateUrl: './sale-invoice-print-page.component.html',
  styleUrl: './sale-invoice-print-page.component.scss',
})
export class SaleInvoicePrintPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly saleService = inject(SaleService);
  private readonly shopService = inject(ShopService);
  private readonly authService = inject(AuthService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly sale = signal<SaleDto | null>(null);
  readonly shop = signal<ShopDetails | null>(null);
  readonly template = signal<InvoiceTemplate>(this.resolveTemplate());

  constructor() {
    this.loadInvoice();
  }

  onPrintAgain(): void {
    window.print();
  }

  onClose(): void {
    window.close();
  }

  private loadInvoice(): void {
    const saleId = this.route.snapshot.paramMap.get('saleId');
    const activeShopId = this.authService.session()?.activeShopId;

    if (!saleId) {
      this.errorMessage.set('Sale was not found.');
      return;
    }

    if (!activeShopId) {
      this.errorMessage.set('Active shop was not found.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    forkJoin({
      sale: this.saleService.getSaleById(saleId),
      shop: this.shopService.getShopDetails(activeShopId),
    }).subscribe({
      next: ({ sale, shop }) => {
        this.sale.set(sale);
        this.shop.set(shop);
        this.isLoading.set(false);
        setTimeout(() => window.print());
      },
      error: (error) => {
        this.errorMessage.set(error.error?.detail || 'Unable to load invoice.');
        this.isLoading.set(false);
      },
    });
  }

  private resolveTemplate(): InvoiceTemplate {
    const template = this.route.snapshot.queryParamMap.get('template')?.toLowerCase();

    return template === 'thermal' ? 'thermal' : 'a4';
  }
}
