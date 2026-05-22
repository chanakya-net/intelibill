import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../../../core/auth/auth.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../../core/storage/offline-sales-device-settings.storage';
import { ShopDetails, ShopService } from '../../../shops/services/shop.service';
import { SaleDto, SaleService } from '../../services/sale.service';
import { SaleInvoiceA4Component } from '../../components/sale-invoice-a4.component';
import { SaleInvoiceThermalComponent } from '../../components/sale-invoice-thermal.component';
import { OfflineSalesQueueIndexedDbService } from '../../services/offline-sales-queue-indexeddb.service';
import { mapOfflineQueuedSaleToSaleDto } from '../../services/offline-sale-invoice.mapper';

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
  private readonly deviceSettingsStorage = inject(OfflineSalesDeviceSettingsStorage);
  private readonly offlineQueueDb = inject(OfflineSalesQueueIndexedDbService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly sale = signal<SaleDto | null>(null);
  readonly shop = signal<ShopDetails | null>(null);
  readonly template = signal<InvoiceTemplate>(this.resolveTemplate());
  readonly pendingSync = signal(false);

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

    if (this.isOfflineInvoice()) {
      this.pendingSync.set(true);
      void this.loadOfflineInvoice(saleId, activeShopId);
      return;
    }

    this.pendingSync.set(false);
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

  private async loadOfflineInvoice(clientSaleId: string, shopId: string): Promise<void> {
    const settings = this.deviceSettingsStorage.loadSettings(shopId);
    const deviceId = settings?.deviceId ?? '';

    if (!deviceId) {
      this.errorMessage.set('Offline device was not found.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    try {
      const queuedSale = await this.offlineQueueDb.getQueuedSale(shopId, deviceId, clientSaleId);
      if (!queuedSale) {
        this.errorMessage.set('Offline invoice was not found.');
        this.isLoading.set(false);
        return;
      }

      this.sale.set(mapOfflineQueuedSaleToSaleDto(queuedSale.payload));
      this.shop.set(this.buildOfflineShopDetails(shopId));
      this.isLoading.set(false);
      setTimeout(() => window.print());
    } catch {
      this.errorMessage.set('Unable to load offline invoice.');
      this.isLoading.set(false);
    }
  }

  private resolveTemplate(): InvoiceTemplate {
    const template = this.route.snapshot.queryParamMap.get('template')?.toLowerCase();

    return template === 'thermal' ? 'thermal' : 'a4';
  }

  private isOfflineInvoice(): boolean {
    return this.route.snapshot.queryParamMap.get('offline') === '1';
  }

  private buildOfflineShopDetails(shopId: string): ShopDetails {
    const session = this.authService.session();
    const activeShop = session?.shops.find((shop) => shop.shopId === shopId)
      ?? session?.shops.find((shop) => shop.isDefault)
      ?? null;

    return {
      shopId,
      name: activeShop?.shopName ?? 'Shop',
      address: '',
      city: '',
      state: '',
      pincode: '',
      contactPerson: null,
      mobileNumber: null,
      gstNumber: null,
      bankName: null,
      bankAccountNumber: null,
      bankAccountType: null,
      ifscCode: null,
      accountHolderName: null,
    };
  }
}
