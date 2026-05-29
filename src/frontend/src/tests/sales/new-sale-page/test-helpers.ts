import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Subject, of } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../../../app/core/auth/auth.service';
import { NetworkStatusService } from '../../../app/core/services/network-status.service';
import { ProductCatalogSyncService } from '../../../app/core/services/product-catalog-sync.service';
import { ShopUpdatePayload, ShopUpdatesSignalRService } from '../../../app/core/services/shop-updates-signalr.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../app/core/storage/offline-sales-device-settings.storage';
import { OfflineSalesSnapshotIndexedDbService } from '../../../app/core/storage/offline-sales-snapshot-indexeddb.service';
import { SalesCartIndexedDbService } from '../../../app/core/storage/sales-cart-indexeddb.service';
import { CustomersFacade } from '../../../app/features/customers/state/customers.facade';
import { InventoryService } from '../../../app/features/inventory/services/inventory.service';
import { OfflineSaleFinalizationService } from '../../../app/features/sales/services/offline-sale-finalization.service';
import { OfflineSalesQueueIndexedDbService } from '../../../app/features/sales/services/offline-sales-queue-indexeddb.service';
import { SaleService } from '../../../app/features/sales/services/sale.service';
import type { SalePreviewDto } from '../../../app/features/sales/services/sale.models';
import { SalesFacade } from '../../../app/features/sales/state/sales.facade';
import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';

export function createQrLikeBarcode(): string {
  // Enough entropy for tests; avoids uuid dependency.
  return `QR-${Math.random().toString(36).slice(2)}-${Date.now()}`;
}

export interface NewSalePageTestDeps {
  readonly inventoryService: {
    getAvailableBatchesBySearchTerm: ReturnType<typeof vi.fn>;
    getProductDetailsByNameOrBarcode: ReturnType<typeof vi.fn>;
  };
  readonly router: { navigate: ReturnType<typeof vi.fn> };
  readonly salesFacade: {
    submitting: ReturnType<typeof signal<boolean>>;
    errorMessage: ReturnType<typeof signal<string>>;
    lastMutationSucceeded: ReturnType<typeof signal<boolean>>;
    lastMutationType: ReturnType<typeof signal<'record-sale' | 'record-return' | 'void-return' | null>>;
    lastRecordedSale: ReturnType<typeof signal<unknown>>;
    clearError: ReturnType<typeof vi.fn>;
    clearMutationStatus: ReturnType<typeof vi.fn>;
    clearLastRecordedSale: ReturnType<typeof vi.fn>;
    recordSale: ReturnType<typeof vi.fn>;
  };
  readonly saleService: { previewSale: ReturnType<typeof vi.fn>; getSellables: ReturnType<typeof vi.fn> };
  readonly authService: {
    session: ReturnType<typeof signal<any>>;
    canUseOfflineSalesAuthGrace: ReturnType<typeof vi.fn>;
  };
  readonly cartStorage: {
    loadCart: ReturnType<typeof vi.fn>;
    saveCart: ReturnType<typeof vi.fn>;
    clearCart: ReturnType<typeof vi.fn>;
  };
  readonly productCatalogSync: {
    filterByName: ReturnType<typeof vi.fn>;
    filterByBarcode: ReturnType<typeof vi.fn>;
  };
  readonly customersFacade: {
    allCustomers: ReturnType<typeof signal<any[]>>;
    loadCustomers: ReturnType<typeof vi.fn>;
  };
  readonly shopUpdatesUpdates$: Subject<ShopUpdatePayload>;
  readonly shopUpdatesService: {
    readonly updates$: Subject<ShopUpdatePayload>;
    startConnection: ReturnType<typeof vi.fn>;
    stopConnection: ReturnType<typeof vi.fn>;
  };
  readonly networkStatus: {
    isOnline: ReturnType<typeof signal<boolean>>;
    canReachApi: ReturnType<typeof signal<boolean>>;
    lastVerifiedAt: ReturnType<typeof signal<Date | null>>;
    isChecking: ReturnType<typeof signal<boolean>>;
    checkConnectivity: ReturnType<typeof vi.fn>;
  };
  readonly offlineFinalization: { finalizeAndQueue: ReturnType<typeof vi.fn> };
  readonly deviceSettingsStorage: {
    getOrCreateDeviceId: ReturnType<typeof vi.fn>;
    loadSettings: ReturnType<typeof vi.fn>;
    updateSettings: ReturnType<typeof vi.fn>;
  };
  readonly offlineSnapshotDb: {
    getUsableSnapshotInfo: ReturnType<typeof vi.fn>;
    getUsableBatches: ReturnType<typeof vi.fn>;
    getUsableCustomers: ReturnType<typeof vi.fn>;
    getUsableDiscountRules: ReturnType<typeof vi.fn>;
  };
  readonly offlineQueueDb: { getStatusCounts: ReturnType<typeof vi.fn> };
}

export function setupNewSalePageTestBed(overrides: Partial<NewSalePageTestDeps> = {}) {
  const inventoryService = overrides.inventoryService ?? {
    getAvailableBatchesBySearchTerm: vi.fn(),
    getProductDetailsByNameOrBarcode: vi.fn(),
  };

  const router = overrides.router ?? { navigate: vi.fn() };

  const salesFacade = overrides.salesFacade ?? {
    submitting: signal(false),
    errorMessage: signal(''),
    lastMutationSucceeded: signal(false),
    lastMutationType: signal<'record-sale' | 'record-return' | 'void-return' | null>(null),
    lastRecordedSale: signal(null),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    clearLastRecordedSale: vi.fn(),
    recordSale: vi.fn(),
  };

  const saleService = overrides.saleService ?? { previewSale: vi.fn(), getSellables: vi.fn() };

  const authService = overrides.authService ?? {
    session: signal({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: { id: 'owner-1', email: 'owner@test.com', phoneNumber: null, firstName: 'Owner', lastName: 'One' },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    }),
    canUseOfflineSalesAuthGrace: vi.fn(async () => true),
  };

  const cartStorage = overrides.cartStorage ?? {
    loadCart: vi.fn(async () => []),
    saveCart: vi.fn(async () => undefined),
    clearCart: vi.fn(async () => undefined),
  };

  const productCatalogSync = overrides.productCatalogSync ?? {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
  };

  const customersFacade = overrides.customersFacade ?? {
    allCustomers: signal([
      { customerId: 'cust-1', name: 'Alice', phoneNumber: '+919999111222', address: null, isActive: true },
      { customerId: 'cust-2', name: 'Bob', phoneNumber: '+919999333444', address: null, isActive: true },
      { customerId: 'cust-3', name: 'Charlie', phoneNumber: '+919999555666', address: null, isActive: false },
    ]),
    loadCustomers: vi.fn(),
  };

  const shopUpdatesUpdates$ = new Subject<ShopUpdatePayload>();
  const shopUpdatesService = overrides.shopUpdatesService ?? {
    get updates$() {
      return shopUpdatesUpdates$;
    },
    startConnection: vi.fn(async () => undefined),
    stopConnection: vi.fn(async () => undefined),
  };

  const networkStatus = overrides.networkStatus ?? {
    isOnline: signal(true),
    canReachApi: signal(true),
    lastVerifiedAt: signal<Date | null>(null),
    isChecking: signal(false),
    checkConnectivity: vi.fn(async () => undefined),
  };

  const offlineFinalization = overrides.offlineFinalization ?? { finalizeAndQueue: vi.fn() };

  const deviceSettingsStorage = overrides.deviceSettingsStorage ?? {
    getOrCreateDeviceId: vi.fn(() => 'device-1'),
    loadSettings: vi.fn(() => null),
    updateSettings: vi.fn(),
  };

  const offlineSnapshotDb = overrides.offlineSnapshotDb ?? {
    getUsableSnapshotInfo: vi.fn(async () => null),
    getUsableBatches: vi.fn(async () => []),
    getUsableCustomers: vi.fn(async () => []),
    getUsableDiscountRules: vi.fn(async () => []),
  };

  const offlineQueueDb = overrides.offlineQueueDb ?? {
    getStatusCounts: vi.fn(async () => ({ Pending: 0, Syncing: 0, Synced: 0, SyncedWithWarnings: 0, NeedsReview: 0, Failed: 0 })),
  };

  inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(
    of([
      {
        barcode: createQrLikeBarcode(),
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
      },
    ])
  );

  saleService.getSellables.mockReturnValue(
    of([
      {
        kind: 'Goods',
        inventoryBatchId: 'batch-1',
        barcode: createQrLikeBarcode(),
        itemName: 'Oreo',
        batchNumber: 'B-01',
        quantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 18,
        taxIncluded: true,
        expiryDate: null,
        hsnCode: '0902',
      },
    ])
  );

  inventoryService.getProductDetailsByNameOrBarcode.mockReturnValue(
    of({
      name: 'Oreo',
      description: '',
      uom: 'Unit',
      costPrice: 0,
      mrp: 0,
      salesPrice: 0,
      supplierId: null,
      supplierName: null,
      hsnCode: '0902',
      taxIncluded: true,
      taxRatePercent: 18,
    })
  );

  saleService.previewSale.mockReturnValue(
    of({
      totalAmount: 50,
      totalTaxableAmount: 42.37,
      totalTaxAmount: 7.63,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 42.37,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    } as SalePreviewDto)
  );

  TestBed.configureTestingModule({
    imports: [NewSalePageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    providers: [
      { provide: AuthService, useValue: authService },
      { provide: CustomersFacade, useValue: customersFacade },
      { provide: ProductCatalogSyncService, useValue: productCatalogSync },
      { provide: InventoryService, useValue: inventoryService },
      { provide: SalesCartIndexedDbService, useValue: cartStorage },
      { provide: Router, useValue: router },
      { provide: SalesFacade, useValue: salesFacade },
      { provide: SaleService, useValue: saleService },
      { provide: ShopUpdatesSignalRService, useValue: shopUpdatesService },
      { provide: NetworkStatusService, useValue: networkStatus },
      { provide: OfflineSaleFinalizationService, useValue: offlineFinalization },
      { provide: OfflineSalesDeviceSettingsStorage, useValue: deviceSettingsStorage },
      { provide: OfflineSalesSnapshotIndexedDbService, useValue: offlineSnapshotDb },
      { provide: OfflineSalesQueueIndexedDbService, useValue: offlineQueueDb },
    ],
  });

  return {
    deps: {
      inventoryService,
      router,
      salesFacade,
      saleService,
      authService,
      cartStorage,
      productCatalogSync,
      customersFacade,
      shopUpdatesService,
      shopUpdatesUpdates$,
      networkStatus,
      offlineFinalization,
      deviceSettingsStorage,
      offlineSnapshotDb,
      offlineQueueDb,
    } satisfies NewSalePageTestDeps,
  };
}
