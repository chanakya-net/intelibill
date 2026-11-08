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
import { SaleService } from '../../services/sale.service';
import type { InvoiceDocument, SaleDto } from '../../services/sale.models';
import { OfflineSaleFinalizationService } from '../../services/offline-sale-finalization.service';
import { buildOfflineInvoiceDocument } from '../../services/offline-invoice-document.builder';
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
    creditNoteAppliedAmount: 0,
    items: [],
    returns: [],
    warnings: [],
    creditNoteRedemptions: [],
    document: null,
    isLegacyDocument: true,
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
          lineType: 'goods',
          serviceId: null,
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
    isAuthenticated: vi.fn(),
    canUseOfflineSalesAuthGrace: vi.fn(),
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
    authService.isAuthenticated.mockReset().mockReturnValue(true);
    authService.canUseOfflineSalesAuthGrace.mockReset().mockResolvedValue(false);
    deviceSettingsStorage.loadSettings.mockReset();
    deviceSettingsStorage.getOrCreateDeviceId.mockReset();
    offlineQueueDb.getQueuedSale.mockReset();
    saleService.getSaleById.mockReturnValue(of(sale));
    shopService.getShopDetails.mockReturnValue(of(shop));
    deviceSettingsStorage.loadSettings.mockReturnValue(null);
    offlineQueueDb.getQueuedSale.mockResolvedValue(null);
    authService.session.mockReturnValue({
      user: { id: 'user-1' },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main Shop', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });
  });

  function queuedRecord(payload: Partial<OfflineQueuedSalePayload> = {}) {
    return {
      shopId: 'shop-1',
      deviceId: 'device-1',
      clientSaleId: 'offline-sale-1',
      status: 'Pending',
      payload: { ...offlineQueuedSalePayload, ...payload },
    } as never;
  }

  afterEach(async () => {
    // Rendering schedules an async font-ready print. Keep the browser API fixture
    // installed until those callbacks finish, then destroy the Angular fixtures.
    await vi.runOnlyPendingTimersAsync();
    TestBed.resetTestingModule();
    await vi.runOnlyPendingTimersAsync();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('loads sale and active shop details before printing', async () => {
    const fixture = createComponent();

    expect(saleService.getSaleById).toHaveBeenCalledWith('sale-1');
    expect(shopService.getShopDetails).toHaveBeenCalledWith('shop-1');
    expect(window.print).not.toHaveBeenCalled();

    fixture.detectChanges();
    await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));

    expect(fixture.componentInstance.sale()).toEqual(sale);
    expect(fixture.componentInstance.shop()).toEqual(shop);
  });

  it('loads queued offline invoice payload when route is offline', async () => {
    deviceSettingsStorage.loadSettings.mockReturnValue({ deviceId: 'device-1' } as never);
    offlineQueueDb.getQueuedSale.mockResolvedValue(queuedRecord());

    const fixture = createComponent('a4', 'offline-sale-1', { offline: '1' });

    fixture.detectChanges();

    expect(deviceSettingsStorage.loadSettings).toHaveBeenCalledWith('shop-1');
    expect(offlineQueueDb.getQueuedSale).toHaveBeenCalledWith('shop-1', 'device-1', 'offline-sale-1');
    expect(saleService.getSaleById).not.toHaveBeenCalled();
    expect(shopService.getShopDetails).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(fixture.componentInstance.sale()?.invoiceNumber).toBe('INV-2026-001'));
    fixture.detectChanges();
    expect(fixture.componentInstance.pendingSync()).toBe(true);
    expect(fixture.componentInstance.sale()?.totalAmount).toBe(47.5);
    expect(fixture.componentInstance.sale()?.paidAmount).toBe(47.5);
    expect(fixture.componentInstance.sale()?.dueAmount).toBe(0);
    expect(fixture.componentInstance.sale()?.items[0].itemName).toBe('Offline Item');
    expect(fixture.componentInstance.sale()?.soldAt).toBe('2026-05-01T10:30:00Z');
    await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));
    expect(fixture.nativeElement.querySelector('.print-state--delivery')?.textContent).toContain(
      'Queued on this device',
    );
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

  it('renders thermal invoice component for thermal template', async () => {
    const fixture = createComponent('thermal');

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const thermalInvoice = fixture.nativeElement.querySelector('app-sale-invoice-thermal');
    const placeholder = fixture.nativeElement.textContent ?? '';

    expect(thermalInvoice).not.toBeNull();
    expect(placeholder).not.toContain('Thermal receipt preview');
    await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));
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

  it('prints again when requested', async () => {
    const fixture = createComponent();
    fixture.detectChanges();
    await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));
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
    expect(fixture.componentInstance.errorMessage()).toBe(
      'sales.invoice.errors.offlineDeviceNotFound',
    );
    expect(window.print).not.toHaveBeenCalled();
  });

  it('loads server-backed invoice when offline route flag is not set', async () => {
    const fixture = createComponent();

    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(saleService.getSaleById).toHaveBeenCalledTimes(1);
    expect(offlineQueueDb.getQueuedSale).not.toHaveBeenCalled();
    expect(fixture.componentInstance.sale()?.saleId).toBe('sale-1');
    await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));
  });

  it('legacy_document_never_invents_gstin: a legacy queued sale borrows no other shop and offers reconciliation', async () => {
    authService.session.mockReturnValue({
      user: { id: 'user-1' },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-2', shopName: 'Other Shop', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });
    deviceSettingsStorage.loadSettings.mockReturnValue({ deviceId: 'device-1' } as never);
    offlineQueueDb.getQueuedSale.mockResolvedValue(queuedRecord());

    const fixture = createComponent('a4', 'offline-sale-1', { offline: '1' });
    await vi.waitFor(() => expect(fixture.componentInstance.sale()).not.toBeNull());
    fixture.detectChanges();

    expect(fixture.componentInstance.sale()?.document).toBeNull();
    expect(fixture.componentInstance.shop()?.name).not.toBe('Other Shop');
    expect(fixture.componentInstance.shop()?.gstNumber).toBeNull();
    const text = fixture.nativeElement.textContent as string;
    expect(text).not.toContain('Other Shop');
    expect(text).toContain('is not a copy of a GST document');
    const link = fixture.nativeElement.querySelector('a.print-state__reconciliation') as HTMLAnchorElement | null;
    expect(link?.getAttribute('href')).toBe('/sales/offline-reconciliation');
    await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));
  });

  it('grace_allows_owned_local_print: an expired token inside offline grace prints the issuer\'s invoice', async () => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(true);
    deviceSettingsStorage.loadSettings.mockReturnValue({ deviceId: 'device-1' } as never);
    offlineQueueDb.getQueuedSale.mockResolvedValue(queuedRecord({ issuedByUserId: 'user-1' }));

    const fixture = createComponent('thermal', 'offline-sale-1', { offline: '1' });
    await vi.waitFor(() => expect(fixture.componentInstance.sale()).not.toBeNull());

    expect(fixture.componentInstance.errorMessage()).toBe('');
    expect(fixture.componentInstance.sale()?.invoiceNumber).toBe('INV-2026-001');
  });

  it.each([
    ['another user issued it', { issuedByUserId: 'user-2' }, true, 'sales.invoice.errors.offlineAccessDenied'],
    ['offline grace has expired', { issuedByUserId: 'user-1' }, false, 'sales.invoice.errors.offlineAccessDenied'],
  ])('wrong_scope_denied: renders no invoice data when %s', async (_name, payload, grace, errorKey) => {
    authService.isAuthenticated.mockReturnValue(false);
    authService.canUseOfflineSalesAuthGrace.mockResolvedValue(grace);
    deviceSettingsStorage.loadSettings.mockReturnValue({ deviceId: 'device-1' } as never);
    offlineQueueDb.getQueuedSale.mockResolvedValue(queuedRecord(payload));

    const fixture = createComponent('a4', 'offline-sale-1', { offline: '1' });
    await vi.waitFor(() => expect(fixture.componentInstance.errorMessage()).toBe(errorKey));
    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    expect(fixture.componentInstance.sale()).toBeNull();
    expect(fixture.componentInstance.shop()).toBeNull();
    expect(fixture.nativeElement.querySelector('app-sale-invoice-a4')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('INV-2026-001');
    expect(window.print).not.toHaveBeenCalled();
  });

  it('waits for the fonts before the automatic print', async () => {
    let fontsReady!: () => void;
    Object.defineProperty(document, 'fonts', {
      configurable: true,
      value: { ready: new Promise<void>((resolve) => (fontsReady = resolve)) },
    });

    try {
      const fixture = createComponent();
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(50);
      expect(window.print).not.toHaveBeenCalled();

      fontsReady();
      await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));
    } finally {
      delete (document as { fonts?: unknown }).fonts;
    }
  });

  it('print_cancel_and_retry_preserve_identity: printing never issues, queues or renumbers anything', async () => {
    const finalization = { finalizeAndQueue: vi.fn() };
    const queueWrites = { savePendingSale: vi.fn(), applySyncResult: vi.fn(), deleteOldSyncedRecords: vi.fn() };
    Object.assign(offlineQueueDb, queueWrites);
    TestBed.configureTestingModule({ providers: [{ provide: OfflineSaleFinalizationService, useValue: finalization }] });
    deviceSettingsStorage.loadSettings.mockReturnValue({ deviceId: 'device-1' } as never);
    const built = buildOfflineInvoiceDocument({
      sellerProfile: {
        seller: {
          legalName: 'Main Shop Pvt Ltd',
          tradeName: 'Main Shop',
          address: '1 Market Road, Mumbai, Maharashtra 400001',
          stateCode: '27',
          stateName: 'Maharashtra',
          gstin: '27AAPFU0939F1ZV',
          taxRegime: 'Regular',
        },
        eInvoiceStatus: 'BelowThreshold',
        seriesCode: 'A',
      },
      fiscalYearStart: 2026,
      soldAt: offlineQueuedSalePayload.soldAt,
      customer: null,
      customerName: 'Walk-in',
      customerPhone: null,
      pricing: offlineQueuedSalePayload.pricing,
    });
    if (!built.ok) throw new Error(built.errorCode);
    const document: InvoiceDocument = { ...built.draft, number: 'INV-2026-001' };
    offlineQueueDb.getQueuedSale.mockResolvedValue({ ...(queuedRecord({ document }) as object), status: 'Failed' } as never);

    const fixture = createComponent('a4', 'offline-sale-1', { offline: '1' });
    await vi.waitFor(() => expect(window.print).toHaveBeenCalledTimes(1));
    fixture.detectChanges();

    // The cashier cancels the browser's dialog and prints twice more.
    fixture.componentInstance.onPrintAgain();
    fixture.componentInstance.onPrintAgain();
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(50);

    expect(window.print).toHaveBeenCalledTimes(3);
    expect(finalization.finalizeAndQueue).not.toHaveBeenCalled();
    expect(queueWrites.savePendingSale).not.toHaveBeenCalled();
    expect(queueWrites.applySyncResult).not.toHaveBeenCalled();
    expect(offlineQueueDb.getQueuedSale).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.sale()?.invoiceNumber).toBe('INV-2026-001');
    expect(fixture.componentInstance.sale()?.document?.number).toBe('INV-2026-001');

    // A failed sync is shown beside the invoice, never on it.
    const delivery = fixture.nativeElement.querySelector('.print-state--delivery') as HTMLElement | null;
    expect(delivery?.textContent).toContain('Sending failed');
    expect(delivery?.closest('.invoice-document')).toBeNull();
    expect(fixture.nativeElement.querySelector('.invoice-document')?.textContent).not.toContain('Sending failed');
  });

  it('hides the controls, delivery state and notices from the printed copy', () => {
    // jsdom applies no media queries, so the print rules are read from the stylesheet itself.
    const scss = readFileSync(
      join(process.cwd(), 'src/app/features/sales/pages/sale-invoice-print-page/sale-invoice-print-page.component.scss'),
      'utf-8',
    );
    const printBlock = scss.slice(scss.indexOf('@media print'));
    const hidden = /([^{}]+)\{\s*display:\s*none;\s*\}/.exec(printBlock);
    const hiddenSelectors = (hidden?.[1] ?? '').split(',').map((selector) => selector.trim());

    expect(hiddenSelectors).toEqual(
      expect.arrayContaining(['.screen-controls', '.print-state--delivery', '.print-state--notice']),
    );
    expect(printBlock).not.toMatch(/\.print-state--delivery\s*\{[^}]*color/);
  });
});
