import { CommonModule } from '@angular/common';
import { Component, Injector, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../../../core/auth/auth.service';
import { ShopDetails, ShopService } from '../../../shops/services/shop.service';
import { SaleService } from '../../services/sale.service';
import type { SaleDto } from '../../services/sale.models';
import { SaleInvoiceA4Component } from '../../components/sale-invoice-a4.component';
import { SaleInvoiceThermalComponent } from '../../components/sale-invoice-thermal.component';
import { OfflineInvoiceAccessService, type OfflineInvoiceAccessDenial } from '../../services/offline-invoice-access.service';
import type { OfflineSaleQueueStatus } from '../../services/offline-sale-core.types';
import { mapOfflineQueuedSaleToSaleDto } from '../../services/offline-sale-invoice.mapper';

type InvoiceTemplate = 'a4' | 'thermal';

const OFFLINE_DENIAL_KEYS: Readonly<Record<OfflineInvoiceAccessDenial, string>> = {
  NO_SESSION: 'sales.invoice.errors.activeShopNotFound',
  NO_DEVICE: 'sales.invoice.errors.offlineDeviceNotFound',
  NOT_FOUND: 'sales.invoice.errors.offlineInvoiceNotFound',
  GRACE_UNAVAILABLE: 'sales.invoice.errors.offlineAccessDenied',
  NOT_OWNER: 'sales.invoice.errors.offlineAccessDenied',
  SCOPE_CHANGED: 'sales.invoice.errors.offlineAccessDenied',
};

/** Where a queued invoice is in its journey to the server; shown beside the invoice, never printed on it. */
const DELIVERY_STATUS_KEYS: Readonly<Record<OfflineSaleQueueStatus, string>> = {
  Pending: 'sales.invoice.delivery.pending',
  Syncing: 'sales.invoice.delivery.syncing',
  Failed: 'sales.invoice.delivery.failed',
  NeedsReview: 'sales.invoice.delivery.needsReview',
  Synced: 'sales.invoice.delivery.synced',
  SyncedWithWarnings: 'sales.invoice.delivery.synced',
};

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
  private readonly offlineInvoiceAccess = inject(OfflineInvoiceAccessService);
  private readonly transloco = inject(TranslocoService);
  private readonly injector = inject(Injector);

  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly sale = signal<SaleDto | null>(null);
  readonly shop = signal<ShopDetails | null>(null);
  readonly template = signal<InvoiceTemplate>(this.resolveTemplate());
  readonly pendingSync = signal(false);
  readonly deliveryStatusKey = signal<string | null>(null);
  readonly isLegacyOfflineInvoice = computed(() => this.pendingSync() && !!this.sale() && !this.sale()?.document);

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
      this.errorMessage.set('sales.invoice.errors.saleNotFound');
      return;
    }

    if (!activeShopId) {
      this.errorMessage.set('sales.invoice.errors.activeShopNotFound');
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
        this.printOnceRendered();
      },
      error: (error) => {
        this.errorMessage.set(error.error?.detail || 'sales.invoice.errors.loadFailed');
        this.isLoading.set(false);
      },
    });
  }

  /** Nothing from the queue reaches page state until the access check has granted this very invoice. */
  private async loadOfflineInvoice(clientSaleId: string, shopId: string): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set('');

    try {
      const access = await this.offlineInvoiceAccess.read(clientSaleId);
      if (!access.granted) {
        this.errorMessage.set(OFFLINE_DENIAL_KEYS[access.reason]);
        this.isLoading.set(false);
        return;
      }

      this.sale.set(mapOfflineQueuedSaleToSaleDto(access.record.payload));
      this.shop.set(this.buildOfflineShopDetails(shopId));
      this.deliveryStatusKey.set(DELIVERY_STATUS_KEYS[access.record.status] ?? null);
      this.isLoading.set(false);
      this.printOnceRendered();
    } catch {
      this.errorMessage.set('sales.invoice.errors.offlineLoadFailed');
      this.isLoading.set(false);
    }
  }

  /**
   * Opens the browser's print dialog once, after the invoice has rendered and its fonts have loaded, so the first copy
   * is laid out as every reprint is. Printing reads only what is on the page: it never issues, queues or renumbers.
   */
  private printOnceRendered(): void {
    afterNextRender(
      () => {
        void this.printWhenFontsReady();
      },
      { injector: this.injector },
    );
  }

  private async printWhenFontsReady(): Promise<void> {
    const fonts = (document as { fonts?: { ready?: Promise<unknown> } }).fonts;
    await fonts?.ready;
    window.print();
  }

  private resolveTemplate(): InvoiceTemplate {
    const template = this.route.snapshot.queryParamMap.get('template')?.toLowerCase();

    return template === 'thermal' ? 'thermal' : 'a4';
  }

  private isOfflineInvoice(): boolean {
    return this.route.snapshot.queryParamMap.get('offline') === '1';
  }

  /**
   * The shop shown on a legacy queued sale, which froze no document: its name only, and only from the shop the sale
   * belongs to. Tax identity is never filled in from the shop as it is now.
   */
  private buildOfflineShopDetails(shopId: string): ShopDetails {
    const activeShop = this.authService.session()?.shops.find((shop) => shop.shopId === shopId) ?? null;

    return {
      shopId,
      name: activeShop?.shopName ?? this.transloco.translate('shops.unknown'),
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
