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
import { OfflineQueuedSalePayload } from '../../services/offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from '../../services/offline-sales-queue-indexeddb.service';

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
      void this.loadOfflineInvoice(saleId, activeShopId);
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

  private async loadOfflineInvoice(clientSaleId: string, shopId: string): Promise<void> {
    const settings = this.deviceSettingsStorage.loadSettings(shopId);
    const deviceId = settings?.deviceId ?? this.deviceSettingsStorage.getOrCreateDeviceId(shopId);

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

      this.sale.set(this.mapOfflineQueuedSale(queuedSale.payload));
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

  private mapOfflineQueuedSale(payload: OfflineQueuedSalePayload): SaleDto {
    return {
      saleId: payload.clientSaleId,
      invoiceNumber: payload.invoiceNumber,
      customerId: payload.customerId,
      customerName: payload.customerName,
      customerPhone: payload.customerPhone,
      paymentMethod: payload.paymentMethod,
      soldAt: payload.soldAt,
      paidAmount: payload.pricing.totals.paidAmount,
      dueAmount: payload.pricing.totals.dueAmount,
      totalBeforeDiscount: payload.pricing.totals.totalBeforeDiscount,
      totalDiscountAmount: payload.pricing.totals.totalDiscount,
      totalAmount: payload.pricing.totals.grandTotal,
      totalTaxAmount: payload.pricing.totals.totalTax,
      items: payload.pricing.lines.map((line) => ({
        saleItemId: line.clientLineId,
        itemId: line.itemId,
        itemName: line.itemName,
        inventoryBatchId: line.inventoryBatchId,
        quantity: line.quantity,
        salesPrice: line.salesPrice,
        originalSalesPrice: line.salesPrice,
        finalSalesPrice: line.lineTotal,
        preTaxAmountBeforeDiscount: line.preTaxAmount,
        itemDiscountAmount: line.itemDiscountAmount,
        saleDiscountAmount: line.saleDiscountAmount,
        taxableAmount: line.taxableAmount,
        taxAmount: line.taxAmount,
        totalAmount: line.lineTotal,
        savingsAmount: line.itemDiscountAmount + line.saleDiscountAmount,
        taxRatePercent: line.taxRatePercent,
        isPriceIncludingTax: line.taxIncluded,
        hasPriceMismatch: false,
        returnedQuantity: 0,
        returnableQuantity: line.quantity,
        returnStatus: 'None',
        hsnCode: line.hsnCode,
      })),
      returns: [],
      warnings: ['Pending sync'],
    };
  }
}
