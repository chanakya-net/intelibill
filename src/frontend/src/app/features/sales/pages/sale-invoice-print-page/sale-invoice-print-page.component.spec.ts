import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../../core/storage/offline-sales-device-settings.storage';
import { ShopService } from '../../../shops/services/shop.service';
import { OfflineQueuedSalePayload } from '../../services/offline-sale-core.types';
import { OfflineSalesQueueIndexedDbService } from '../../services/offline-sales-queue-indexeddb.service';
import { SaleDto, SaleService } from '../../services/sale.service';
import { SaleInvoicePrintPageComponent } from './sale-invoice-print-page.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8') as string) as Record<string, unknown>;

describe('SaleInvoicePrintPageComponent', () => {
  const sale: SaleDto = {
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    customerId: null,
    customerName: 'Walk-in',
    customerPhone: null,
    paymentMethod: 1,
    soldAt: '2026-05-01T10:00:00Z',
    paidAmount: 100,
    dueAmount: 0,
    totalBeforeDiscount: 100,
    totalDiscountAmount: 0,
    totalAmount: 100,
    totalTaxAmount: 5,
    items: [],
    returns: [],
    warnings: [],
  };

  const shop = {
    shopId: 'shop-1',
    name: 'Main Shop',
    address: '1 Market Road',
    city: 'Mumbai',
    state: 'MH',
    pincode: '400001',
    contactPerson: 'Owner',
    mobileNumber: '9000000000',
    gstNumber: 'GSTIN',
    bankName: null,
    bankAccountNumber: null,
    bankAccountType: null,
    ifscCode: null,
    accountHolderName: null,
  };

  const offlineQueuedSalePayload: OfflineQueuedSalePayload = {
    clientSaleId: 'offline-sale-1',
    idempotencyKey: 'offline-sale-offline-sale-1',
    shopId: 'shop-1',
    deviceId: 'device-1',
    invoiceNumber: 'INV-2026-001',
    soldAt: '2026-05-01T10:30:00Z',
    pricing: {
      lines: [
        {
          clientLineId: 'line-1',
          inventoryBatchId: 'batch-1',
          itemId: 'item-1',
          barcode: '123',
          itemName: 'Offline Item',
          batchNumber: 'B1',
          quantity: 1,
          salesPrice: 50,
          mrp: 60,
          costPrice: 20,
          taxRatePercent: 5,
          taxIncluded: true,
          hsnCode: null,
          preTaxAmount: 47.62,
          itemDiscountAmount: 2.38,
          saleDiscountAmount: 0,
          taxableAmount: 45.24,
          taxAmount: 2.26,
          lineTotal: 47.5,
          configuredRuleId: null,
        },
      ],
      totals: {
        totalBeforeDiscount: 50,
        totalDiscount: 2.38,
        totalTax: 2.26,
        grandTotal: 47.5,
        paidAmount: 47.5,
        dueAmount: 0,
      },
    },
    paymentMethod: 1,
    customerId: null,
    customerName: 'Walk-in',
    customerPhone: null,
  };

  const saleService = {
    getSaleById: vi.fn(),
  };

  const shopService = {
    getShopDetails: vi.fn(),
  };

  const authService = {
    session: vi.fn(),
  };

  const deviceSettingsStorage = {
    loadSettings: vi.fn(),
    getOrCreateDeviceId: vi.fn(),
  };

  const offlineQueueDb = {
    getQueuedSale: vi.fn(),
  };

  const createActivatedRoute = (template?: string, saleId = 'sale-1', queryParams: Record<string, string> = {}) => ({
    snapshot: {
      paramMap: convertToParamMap({ saleId }),
      queryParamMap: convertToParamMap({ ...(template === undefined ? {} : { template }), ...queryParams }),
    },
  });

  const createComponent = (
    template?: string,
    saleId = 'sale-1',
    queryParams: Record<string, string> = {}
  ): ComponentFixture<SaleInvoicePrintPageComponent> => {
    TestBed.configureTestingModule({
      imports: [
        CommonModule,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
        SaleInvoicePrintPageComponent,
      ],
      providers: [
        { provide: ActivatedRoute, useValue: createActivatedRoute(template, saleId, queryParams) },
        { provide: SaleService, useValue: saleService },
        { provide: ShopService, useValue: shopService },
        { provide: AuthService, useValue: authService },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: deviceSettingsStorage },
        { provide: OfflineSalesQueueIndexedDbService, useValue: offlineQueueDb },
      ],
    });

    return TestBed.createComponent(SaleInvoicePrintPageComponent);
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
    saleService.getSaleById.mockReset();
    shopService.getShopDetails.mockReset();
    authService.session.mockReset();
    deviceSettingsStorage.loadSettings.mockReset();
    deviceSettingsStorage.getOrCreateDeviceId.mockReset();
    offlineQueueDb.getQueuedSale.mockReset();
    saleService.getSaleById.mockReturnValue(of(sale));
    shopService.getShopDetails.mockReturnValue(of(shop));
    deviceSettingsStorage.loadSettings.mockReturnValue(null);
    offlineQueueDb.getQueuedSale.mockResolvedValue(null);
    authService.session.mockReturnValue({
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main Shop', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });
  });

  afterEach(() => {
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('loads sale and active shop details before printing', () => {
    const fixture = createComponent();

    expect(saleService.getSaleById).toHaveBeenCalledWith('sale-1');
    expect(shopService.getShopDetails).toHaveBeenCalledWith('shop-1');
    expect(window.print).not.toHaveBeenCalled();

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(fixture.componentInstance.sale()).toEqual(sale);
    expect(fixture.componentInstance.shop()).toEqual(shop);
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('loads queued offline invoice payload when route is offline', async () => {
    deviceSettingsStorage.loadSettings.mockReturnValue({ deviceId: 'device-1' } as never);
    offlineQueueDb.getQueuedSale.mockResolvedValue({
      payload: offlineQueuedSalePayload,
    } as never);

    const fixture = createComponent('a4', 'offline-sale-1', { offline: '1' });

    fixture.detectChanges();

    expect(deviceSettingsStorage.loadSettings).toHaveBeenCalledWith('shop-1');
    expect(offlineQueueDb.getQueuedSale).toHaveBeenCalledWith('shop-1', 'device-1', 'offline-sale-1');
    expect(saleService.getSaleById).not.toHaveBeenCalled();
    expect(shopService.getShopDetails).not.toHaveBeenCalled();
    await Promise.resolve();
    expect(fixture.componentInstance.sale()?.invoiceNumber).toBe('INV-2026-001');
    expect(fixture.componentInstance.pendingSync()).toBe(true);
    expect(fixture.componentInstance.sale()?.totalAmount).toBe(47.5);
    expect(fixture.componentInstance.sale()?.paidAmount).toBe(47.5);
    expect(fixture.componentInstance.sale()?.dueAmount).toBe(0);
    expect(fixture.componentInstance.sale()?.items[0].itemName).toBe('Offline Item');
    expect(fixture.componentInstance.sale()?.soldAt).toBe('2026-05-01T10:30:00Z');
    vi.runOnlyPendingTimers();
    expect(window.print).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('Sale Queued for Sync');
  });

  it('defaults missing or invalid template values to A4', () => {
    const missingFixture = createComponent();

    expect(missingFixture.componentInstance.template()).toBe('a4');

    TestBed.resetTestingModule();
    const invalidFixture = createComponent('unknown');

    expect(invalidFixture.componentInstance.template()).toBe('a4');
  });

  it('selects explicit A4 template', () => {
    const fixture = createComponent('a4');

    expect(fixture.componentInstance.template()).toBe('a4');
  });

  it('selects thermal template', () => {
    const fixture = createComponent('thermal');

    expect(fixture.componentInstance.template()).toBe('thermal');
  });

  it('renders thermal invoice component for thermal template', () => {
    const fixture = createComponent('thermal');

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const thermalInvoice = fixture.nativeElement.querySelector('app-sale-invoice-thermal');
    const placeholder = fixture.nativeElement.textContent ?? '';

    expect(thermalInvoice).not.toBeNull();
    expect(placeholder).not.toContain('Thermal receipt preview');
  });

  it('sets an error and does not print when data loading fails', () => {
    saleService.getSaleById.mockReturnValue(throwError(() => ({ error: { detail: 'Sale missing' } })));

    const fixture = createComponent();
    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(fixture.componentInstance.errorMessage()).toBe('Sale missing');
    expect(fixture.componentInstance.isLoading()).toBe(false);
    expect(window.print).not.toHaveBeenCalled();
  });

  it('prints again when requested', () => {
    const fixture = createComponent();
    fixture.detectChanges();
    vi.runOnlyPendingTimers();
    vi.mocked(window.print).mockClear();

    fixture.componentInstance.onPrintAgain();

    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('does not create a device id when offline invoice settings are missing', async () => {
    const fixture = createComponent(undefined, 'offline-sale-1', { offline: '1' });
    await Promise.resolve();

    expect(deviceSettingsStorage.loadSettings).toHaveBeenCalledWith('shop-1');
    expect(deviceSettingsStorage.getOrCreateDeviceId).not.toHaveBeenCalled();
    expect(offlineQueueDb.getQueuedSale).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toBe('Offline device was not found.');
    expect(window.print).not.toHaveBeenCalled();
  });

  it('loads server-backed invoice when offline route flag is not set', () => {
    const fixture = createComponent();

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(saleService.getSaleById).toHaveBeenCalledTimes(1);
    expect(offlineQueueDb.getQueuedSale).not.toHaveBeenCalled();
    expect(fixture.componentInstance.sale()?.saleId).toBe('sale-1');
  });
});
