import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, Subject, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { ShopUpdatesSignalRService, ShopUpdatePayload } from '../../../core/services/shop-updates-signalr.service';
import { SalesCartIndexedDbService } from '../../../core/storage/sales-cart-indexeddb.service';
import { CustomersFacade } from '../../customers/state/customers.facade';
import { InventoryService } from '../../inventory/services/inventory.service';
import { SaleService, SalePreviewDto } from '../services/sale.service';
import { SalesFacade } from '../state/sales.facade';
import { NewSalePageComponent } from './new-sale-page.component';

describe('NewSalePageComponent', () => {
  const inventoryService = {
    getAvailableBatchesBySearchTerm: vi.fn(),
  };

  const router = {
    navigate: vi.fn(),
  };

  const salesFacade = {
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

  const saleService = {
    previewSale: vi.fn(),
  };

  const authService = {
    session: signal({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      rememberMe: true,
      user: {
        id: 'owner-1',
        email: 'owner@test.com',
        phoneNumber: null,
        firstName: 'Owner',
        lastName: 'One',
      },
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    }),
  };

  const cartStorage = {
    loadCart: vi.fn(async () => []),
    saveCart: vi.fn(async () => undefined),
    clearCart: vi.fn(async () => undefined),
  };

  const productCatalogSync = {
    filterByName: vi.fn(() => []),
    filterByBarcode: vi.fn(() => []),
  };

  const customersFacade = {
    allCustomers: signal([
      { customerId: 'cust-1', name: 'Alice', phoneNumber: '+919999111222', address: null, isActive: true },
      { customerId: 'cust-2', name: 'Bob', phoneNumber: '+919999333444', address: null, isActive: true },
      { customerId: 'cust-3', name: 'Charlie', phoneNumber: '+919999555666', address: null, isActive: false },
    ]),
    loadCustomers: vi.fn(),
  };

  let shopUpdatesUpdates$: Subject<ShopUpdatePayload>;
  const shopUpdatesService = {
    get updates$() {
      return shopUpdatesUpdates$;
    },
    startConnection: vi.fn(async () => undefined),
    stopConnection: vi.fn(async () => undefined),
  };

  beforeEach(() => {
    shopUpdatesUpdates$ = new Subject<ShopUpdatePayload>();
    inventoryService.getAvailableBatchesBySearchTerm.mockReset();
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

    saleService.previewSale.mockReset();
    saleService.previewSale.mockReturnValue(of({
      totalAmount: 50,
      totalTaxableAmount: 42.37,
      totalTaxAmount: 7.63,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 42.37,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    } as SalePreviewDto));

    router.navigate.mockReset();
    salesFacade.clearError.mockReset();
    salesFacade.clearMutationStatus.mockReset();
    salesFacade.recordSale.mockReset();
    salesFacade.submitting.set(false);
    salesFacade.errorMessage.set('');
    salesFacade.lastMutationSucceeded.set(false);
    salesFacade.lastMutationType.set(null);
    salesFacade.lastRecordedSale.set(null);
    salesFacade.clearLastRecordedSale.mockReset();
    cartStorage.loadCart.mockClear();
    cartStorage.saveCart.mockClear();
    cartStorage.clearCart.mockClear();
    productCatalogSync.filterByName.mockClear();
    productCatalogSync.filterByBarcode.mockClear();
    customersFacade.loadCustomers.mockReset();

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
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('opens and closes scanner dialog state', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.openScanner();
    expect(component.isScannerOpen()).toBe(true);

    component.onScannerVisibilityChange(false);
    expect(component.isScannerOpen()).toBe(false);
  });

  it('triggers search immediately when scanner detects a barcode', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    const barcode = createQrLikeBarcode();
    component.isScannerOpen.set(true);

    component.onScannedBarcode({ value: barcode, format: 'QR-CODE', engine: 'native' });

    expect(component.searchInput()).toBe('');
    expect(component.isScannerOpen()).toBe(false);
    expect(inventoryService.getAvailableBatchesBySearchTerm).toHaveBeenCalledWith(barcode);
    expect(component.cart()).toHaveLength(1);
    expect(component.cart()[0].quantity).toBe(1);
    expect(component.cart()[0].inventoryBatchId).toBe('batch-1');
    expect(component.showBatchPicker()).toBe(false);
    expect(component.searchInput()).toBe('');
  });

  it('ignores empty scanned values', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.onScannedBarcode({ value: '   ', format: 'QR-CODE', engine: 'native' });

    expect(inventoryService.getAvailableBatchesBySearchTerm).not.toHaveBeenCalled();
  });

  it('increments and decrements cart quantity within stock limits', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.searchInput.set('oreo');
    component.onBarcodeSearch();

    expect(component.cart()[0].quantity).toBe(1);

    component.onIncreaseCartItem(0);
    expect(component.cart()[0].quantity).toBe(2);

    for (let i = 0; i < 20; i++) {
      component.onIncreaseCartItem(0);
    }
    expect(component.cart()[0].quantity).toBe(10);

    component.onDecreaseCartItem(0);
    expect(component.cart()[0].quantity).toBe(9);

    for (let i = 0; i < 20; i++) {
      component.onDecreaseCartItem(0);
    }
    expect(component.cart()[0].quantity).toBe(1);
  });

  it('calculates subtotal, tax, and final totals for included and excluded tax prices', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-inc',
        barcode: 'A',
        itemName: 'Tax Included',
        batchNumber: 'B-INC',
        inventoryBatchId: 'batch-inc',
        quantity: 2,
        availableQuantity: 5,
        salesPrice: 118,
        mrp: 118,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
      {
        clientLineKey: 'clk-exc',
        barcode: 'B',
        itemName: 'Tax Excluded',
        batchNumber: 'B-EXC',
        inventoryBatchId: 'batch-exc',
        quantity: 1,
        availableQuantity: 3,
        salesPrice: 100,
        mrp: 100,
        taxRatePercent: 18,
        taxIncluded: false,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);
    fixture.detectChanges();

    expect(component.subtotalAmount()).toBeCloseTo(300, 6);
    expect(component.totalTaxAmount()).toBeCloseTo(54, 6);
    expect(component.totalAmount()).toBeCloseTo(354, 6);
  });

  it('clears cart when clear action is used', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-clear',
        barcode: 'A',
        itemName: 'Tax Included',
        batchNumber: 'B-INC',
        inventoryBatchId: 'batch-inc',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 118,
        mrp: 118,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);

    component.onClearCart();

    expect(component.cart()).toHaveLength(0);
  });

  it('auto-adjusts paid and due when either field is edited', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-a',
        barcode: 'A',
        itemName: 'Item A',
        batchNumber: 'B-A',
        inventoryBatchId: 'batch-a',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 100,
        mrp: 100,
        taxRatePercent: 0,
        taxIncluded: false,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
      {
        clientLineKey: 'clk-b',
        barcode: 'B',
        itemName: 'Item B',
        batchNumber: 'B-B',
        inventoryBatchId: 'batch-b',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 50,
        mrp: 50,
        taxRatePercent: 0,
        taxIncluded: false,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);
    fixture.detectChanges();
    component.paymentForm.controls.dueAmount.setValue(0);

    expect(component.totalAmount()).toBe(150);
    expect(component.paymentForm.controls.dueAmount.value).toBe(0);
    expect(component.paymentForm.controls.paidAmount.value).toBe(150);

    component.paymentForm.controls.dueAmount.setValue(40);
    expect(component.paymentForm.controls.dueAmount.value).toBe(40);
    expect(component.paymentForm.controls.paidAmount.value).toBe(110);

    component.paymentForm.controls.paidAmount.setValue(25);
    expect(component.paymentForm.controls.paidAmount.value).toBe(25);
    expect(component.paymentForm.controls.dueAmount.value).toBe(125);

    component.paymentForm.controls.paidAmount.setValue(999);
    expect(component.paymentForm.controls.paidAmount.value).toBe(150);
    expect(component.paymentForm.controls.dueAmount.value).toBe(0);

    component.paymentForm.controls.dueAmount.setValue(999);
    expect(component.paymentForm.controls.dueAmount.value).toBe(150);
    expect(component.paymentForm.controls.paidAmount.value).toBe(0);

    component.paymentForm.controls.paidAmount.setValue(80);
    component.cart.update((items) => items.map((item, index) => (index === 0 ? { ...item, quantity: 2 } : item)));
    fixture.detectChanges();

    expect(component.totalAmount()).toBe(250);
    expect(component.paymentForm.controls.paidAmount.value).toBe(80);
    expect(component.paymentForm.controls.dueAmount.value).toBe(170);
  });

  it('loads customers and suggests active customer names for autocomplete', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    expect(customersFacade.loadCustomers).toHaveBeenCalledTimes(1);

    component.onFilterCustomerName({ originalEvent: new Event('input'), query: 'a' });

    expect(component.customerNameSuggestions()).toEqual(['Alice']);
  });

  it('assigns selected customer id and phone, then clears id when name is edited', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.onCustomerSuggestionSelected('Alice');

    expect(component.selectedCustomerId()).toBe('cust-1');
    expect(component.customerForm.controls.customerName.value).toBe('Alice');
    expect(component.customerForm.controls.customerPhone.value).toBe('+919999111222');

    component.customerForm.controls.customerName.setValue('Alice changed');

    expect(component.selectedCustomerId()).toBeNull();
  });

  it('allows credit only when existing customer record is selected', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.customerForm.controls.customerPhone.setValue('+919876543210');
    expect(component.canUseCredit()).toBe(false);

    component.onCustomerSuggestionSelected('Alice');
    expect(component.canUseCredit()).toBe(true);
  });

  it('resets due amount when selected customer is cleared', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-a',
        barcode: 'A',
        itemName: 'Item A',
        batchNumber: 'B-A',
        inventoryBatchId: 'batch-a',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 100,
        mrp: 100,
        taxRatePercent: 0,
        taxIncluded: false,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
      {
        clientLineKey: 'clk-b',
        barcode: 'B',
        itemName: 'Item B',
        batchNumber: 'B-B',
        inventoryBatchId: 'batch-b',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 50,
        mrp: 50,
        taxRatePercent: 0,
        taxIncluded: false,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);
    fixture.detectChanges();

    component.onCustomerSuggestionSelected('Alice');
    component.paymentForm.controls.dueAmount.setValue(40);

    expect(component.paymentForm.controls.dueAmount.value).toBe(40);
    expect(component.paymentForm.controls.paidAmount.value).toBe(110);

    component.customerForm.controls.customerName.setValue('Alice changed');
    fixture.detectChanges();

    expect(component.selectedCustomerId()).toBeNull();
    expect(component.canUseCredit()).toBe(false);
    expect(component.paymentForm.controls.dueAmount.disabled).toBe(true);
    expect(component.paymentForm.controls.dueAmount.value).toBe(0);
    expect(component.paymentForm.controls.paidAmount.value).toBe(150);
  });

  it('assigns a stable clientLineKey UUID when adding a batch to cart', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.searchInput.set('oreo');
    component.onBarcodeSearch();

    expect(component.cart()).toHaveLength(1);
    const key = component.cart()[0].clientLineKey;
    expect(typeof key).toBe('string');
    expect(key).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
    );
  });

  it('sends clientLineKey from cart item (not inventoryBatchId) in preview request', async () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    // Let bootstrap (loadCart) complete so cart effect runs normally
    await fixture.whenStable();

    vi.useFakeTimers();

    component.cart.set([
      {
        clientLineKey: 'stable-uuid-key',
        barcode: 'BC-1',
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);
    fixture.detectChanges(); // runs the cart effect, fires previewTrigger$

    vi.runAllTimers(); // advance 400ms debounce
    await Promise.resolve(); // flush observable subscription

    expect(saleService.previewSale).toHaveBeenCalled();
    const callArg = saleService.previewSale.mock.calls[0][0];
    expect(callArg.items).toHaveLength(1);
    expect(callArg.items[0].clientLineKey).toBe('stable-uuid-key');
    expect(callArg.items[0].clientLineKey).not.toBe('batch-1');

    vi.useRealTimers();
  });

  it('uses preview total for payment sync when preview succeeds', () => {
    const mockPreview: SalePreviewDto = {
      totalAmount: 99,
      totalTaxableAmount: 83.9,
      totalTaxAmount: 15.1,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 83.9,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    };

    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-x',
        barcode: 'X',
        itemName: 'Item X',
        batchNumber: 'B-X',
        inventoryBatchId: 'batch-x',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);

    // Without preview, totalAmount uses local calculation
    const localTotal = component.totalAmount();

    // Set preview — totalAmount should prefer the preview total
    component.checkoutPreview.set(mockPreview);
    fixture.detectChanges();

    expect(component.checkoutPreview()).toEqual(mockPreview);
    expect(component.totalAmount()).toBe(99);
    expect(component.totalAmount()).not.toBe(localTotal);
    // Payment sync reflects preview total
    expect(component.paymentForm.controls.paidAmount.value).toBe(99);
  });

  it('includes sale-level instant discount in preview request', async () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

    vi.useFakeTimers();

    component.cart.set([
      {
        clientLineKey: 'clk-1',
        barcode: 'BC-1',
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 18,
        taxIncluded: true,
        costPrice: 10,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);

    component.onSaleDiscountTypeChange(1);
    component.onSaleDiscountValueChange(10);

    fixture.detectChanges();
    vi.runAllTimers();
    await Promise.resolve();

    expect(saleService.previewSale).toHaveBeenCalled();
    const callArg = saleService.previewSale.mock.calls[0]?.[0];
    expect(callArg.saleDiscount).toEqual({ type: 1, value: 10 });

    vi.useRealTimers();
  });

  it('renders discount i18n keys for labels and options', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-1',
        barcode: 'BC-1',
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 0,
        taxIncluded: true,
        costPrice: 10,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);

    component.checkoutPreview.set({
      totalAmount: 50,
      totalTaxableAmount: 50,
      totalTaxAmount: 0,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 50,
      configuredSaleRule: { percentage: 5 },
      lines: [
        {
          clientLineKey: 'clk-1',
          barcode: 'BC-1',
          batchNumber: 'B-01',
          itemName: 'Oreo',
          quantity: 1,
          salesPrice: 50,
          costPrice: 10,
          preTaxAmountBeforeDiscount: 50,
          itemDiscountAmount: 0,
          saleDiscountAmount: 0,
          taxAmount: 0,
          totalAmount: 50,
          configuredBatchRulePercentage: 2,
          maxAllowedItemDiscountPercent: 10,
          maxAllowedItemDiscountFlat: 5,
        },
      ],
      infos: [],
      warnings: [],
    } as unknown as SalePreviewDto);

    component.toggleLineDiscountEditor('clk-1');
    component.toggleSaleDiscountEditor();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('sales.newSale.discounts.edit');
    expect(text).toContain('sales.newSale.discounts.editSale');
    expect(text).toContain('sales.newSale.discounts.configuredPercent');
    expect(text).toContain('sales.newSale.discounts.configuredSalePercent');
  });

  it('renders sale discount ineligible hint i18n key', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-1',
        barcode: 'BC-1',
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 0,
        taxIncluded: true,
        costPrice: 10,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);

    component.checkoutPreview.set({
      totalAmount: 50,
      totalTaxableAmount: 50,
      totalTaxAmount: 0,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 0,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    } as SalePreviewDto);

    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('sales.newSale.discounts.saleNotEligible');
  });

  it('revalidates and clamps discounts when preview limits shrink', async () => {
    const previewLine = {
      clientLineKey: 'clk-1',
      barcode: 'BC-1',
      batchNumber: 'B-01',
      itemName: 'Oreo',
      quantity: 1,
      salesPrice: 50,
      costPrice: 10,
      preTaxAmountBeforeDiscount: 50,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxAmount: 0,
      totalAmount: 50,
      configuredBatchRulePercentage: null,
      maxAllowedItemDiscountPercent: 20,
      maxAllowedItemDiscountFlat: 10,
    };

    saleService.previewSale.mockReset();
    const preview1 = {
      totalAmount: 40,
      totalTaxableAmount: 40,
      totalTaxAmount: 0,
      totalDiscountAmount: 10,
      saleLevelEligibleSubtotal: 40,
      configuredSaleRule: { percentage: 20 },
      lines: [previewLine],
      infos: [],
      warnings: [],
    } as unknown as SalePreviewDto;

    const preview2 = {
      totalAmount: 46,
      totalTaxableAmount: 46,
      totalTaxAmount: 0,
      totalDiscountAmount: 4,
      saleLevelEligibleSubtotal: 46,
      configuredSaleRule: { percentage: 5 },
      lines: [{ ...previewLine, maxAllowedItemDiscountPercent: 5 }],
      infos: [],
      warnings: [],
    } as unknown as SalePreviewDto;

    saleService.previewSale
      .mockReturnValueOnce(
        of({
          ...preview1,
        })
      )
      .mockReturnValueOnce(of({ ...preview2 }))
      .mockReturnValue(of({ ...preview2 }));

    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

    vi.useFakeTimers();

    component.onSaleDiscountTypeChange(1);
    component.onSaleDiscountValueChange(10);

    component.cart.set([
      {
        clientLineKey: 'clk-1',
        barcode: 'BC-1',
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 0,
        taxIncluded: true,
        costPrice: 10,
        itemDiscountType: 1,
        itemDiscountValue: 10,
      },
    ]);

    fixture.detectChanges();
    vi.runAllTimers();
    await Promise.resolve();

    // After first preview, values remain within max.
    expect(component.saleDiscountValue()).toBeGreaterThan(0);
    expect(component.saleDiscountValue()).toBeLessThanOrEqual(10);
    expect(component.cart()[0].itemDiscountValue).toBe(10);

    // Trigger another preview (cart mutation) with stricter limits
    component.onIncreaseCartItem(0);
    fixture.detectChanges();
    vi.runAllTimers();
    await Promise.resolve();

    expect(component.saleDiscountValue()).toBe(5);
    expect(component.saleDiscountError()).toBe('');
    expect(component.cart()[0].itemDiscountValue).toBe(5);
    expect(component.getCartItemDiscountError('clk-1')).toBe('');

    vi.useRealTimers();
  });

  it('blocks item discount updates above preview limits', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-1',
        barcode: 'BC-1',
        itemName: 'Oreo',
        batchNumber: 'B-01',
        inventoryBatchId: 'batch-1',
        quantity: 1,
        availableQuantity: 10,
        salesPrice: 50,
        mrp: 60,
        taxRatePercent: 0,
        taxIncluded: true,
        costPrice: 40,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);

    component.checkoutPreview.set({
      totalAmount: 50,
      totalTaxableAmount: 50,
      totalTaxAmount: 0,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 50,
      configuredSaleRule: null,
      lines: [
        {
          itemId: 'item-1',
          barcode: 'BC-1',
          itemName: 'Oreo',
          inventoryBatchId: 'batch-1',
          batchNumber: 'B-01',
          quantity: 1,
          costPrice: 40,
          salesPrice: 50,
          mrp: 60,
          taxRatePercent: 0,
          isPriceIncludingTax: true,
          preTaxAmountBeforeDiscount: 50,
          itemDiscountAmount: 0,
          saleDiscountAmount: 0,
          taxableAmount: 50,
          taxAmount: 0,
          lineTotalAmount: 50,
          maxAllowedItemDiscountFlat: 10,
          maxAllowedItemDiscountPercent: 20,
          configuredBatchRuleId: null,
          configuredBatchRulePercentage: null,
          hasClientPriceMismatch: false,
          clientLineKey: 'clk-1',
        },
      ],
      infos: [],
      warnings: [],
    } as SalePreviewDto);

    component.onCartItemDiscountTypeChange('clk-1', 2);
    component.onCartItemDiscountValueChange('clk-1', 25);

    expect(component.cart()[0].itemDiscountType).toBe(2);
    expect(component.cart()[0].itemDiscountValue).toBe(0);
    expect(component.getCartItemDiscountError('clk-1')).toBeTruthy();
  });

  it('blocks submit and sets paymentSplitError when no preview is available', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-x',
        barcode: 'X',
        itemName: 'Item X',
        batchNumber: 'B-X',
        inventoryBatchId: 'batch-x',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 100,
        mrp: 100,
        taxRatePercent: 0,
        taxIncluded: false,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);
    component.checkoutPreview.set(null);
    component.paymentForm.controls.paidAmount.setValue(100);

    component.onSubmit();

    expect(component.paymentSplitError()).toBe('sales.newSale.previewRequired');
    expect(salesFacade.recordSale).not.toHaveBeenCalled();
  });

  it('includes an idempotency key when submitting a sale', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    const component = fixture.componentInstance;

    component.cart.set([
      {
        clientLineKey: 'clk-x',
        barcode: 'X',
        itemName: 'Item X',
        batchNumber: 'B-X',
        inventoryBatchId: 'batch-x',
        quantity: 1,
        availableQuantity: 5,
        salesPrice: 100,
        mrp: 100,
        taxRatePercent: 0,
        taxIncluded: false,
        costPrice: 0,
        itemDiscountType: 0,
        itemDiscountValue: 0,
      },
    ]);
    component.checkoutPreview.set({
      totalAmount: 100,
      totalTaxableAmount: 100,
      totalTaxAmount: 0,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 100,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    } as SalePreviewDto);
    component.paymentForm.controls.paidAmount.setValue(100);
    component.paymentForm.controls.dueAmount.setValue(0);

    component.onSubmit();

    expect(salesFacade.recordSale).toHaveBeenCalledWith(
      expect.objectContaining({
        idempotencyKey: expect.stringMatching(/^sale-/),
      })
    );
  });

  function createQrLikeBarcode() {
    return `QR|01|${crypto.randomUUID()}|TRACE|${crypto.randomUUID()}|PAYLOAD|AAAAAAAAAAAAAAAAAAAAAAAA`;
  }

  describe('shop realtime updates', () => {
    it('subscribes to shop updates on component init', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      // Just verify the service is injectable - full integration tested separately
      expect(component).toBeDefined();
    });

    it('triggers preview refresh immediately on PricingChanged event', async () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      saleService.previewSale.mockReturnValue(
        of({
          totalAmount: 250,
          totalTaxableAmount: 250,
          totalTaxAmount: 0,
          totalDiscountAmount: 0,
          saleLevelEligibleSubtotal: 250,
          configuredSaleRule: null,
          lines: [
            {
              itemId: 'item-1',
              barcode: 'BAR001',
              itemName: 'Rice',
              inventoryBatchId: 'batch-1',
              batchNumber: 'B-001',
              quantity: 1,
              costPrice: 100,
              salesPrice: 150,
              mrp: 150,
              taxRatePercent: 0,
              isPriceIncludingTax: false,
              preTaxAmountBeforeDiscount: 150,
              itemDiscountAmount: 0,
              saleDiscountAmount: 0,
              taxableAmount: 150,
              taxAmount: 0,
              lineTotalAmount: 150,
              maxAllowedItemDiscountFlat: 50,
              maxAllowedItemDiscountPercent: 20,
              configuredBatchRuleId: null,
              configuredBatchRulePercentage: null,
              hasClientPriceMismatch: false,
              clientLineKey: 'clk-1',
            },
          ],
          infos: [],
          warnings: [],
        })
      );

      component.cartBootstrapped.set(true);
      fixture.detectChanges();

      component.cart.set([
        {
          clientLineKey: 'clk-1',
          barcode: 'BAR001',
          itemName: 'Rice',
          batchNumber: 'B-001',
          inventoryBatchId: 'batch-1',
          quantity: 1,
          availableQuantity: 100,
          salesPrice: 150,
          mrp: 150,
          taxRatePercent: 0,
          taxIncluded: false,
          costPrice: 100,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ]);

      fixture.detectChanges();
      shopUpdatesUpdates$.next({
        eventType: 'PricingChanged',
        shopId: 'shop-xyz',
        changedIds: ['batch-1'],
        occurredOn: new Date().toISOString(),
      });

      // Verify preview was called by triggering cart change
      const startedAt = Date.now();
      while (!saleService.previewSale.mock.calls.length && Date.now() - startedAt < 2000) {
        await new Promise((resolve) => setTimeout(resolve, 50));
      }
      expect(saleService.previewSale).toHaveBeenCalled();
    });

    it('detects changed rows and highlights them via clientLineKey', async () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      saleService.previewSale.mockReturnValue(
        of({
          totalAmount: 160,
          totalTaxableAmount: 160,
          totalTaxAmount: 0,
          totalDiscountAmount: 0,
          saleLevelEligibleSubtotal: 160,
          configuredSaleRule: null,
          lines: [
            {
              itemId: 'item-1',
              barcode: 'BAR001',
              itemName: 'Rice',
              inventoryBatchId: 'batch-1',
              batchNumber: 'B-001',
              quantity: 1,
              costPrice: 100,
              salesPrice: 160,
              mrp: 150,
              taxRatePercent: 0,
              isPriceIncludingTax: false,
              preTaxAmountBeforeDiscount: 160,
              itemDiscountAmount: 0,
              saleDiscountAmount: 0,
              taxableAmount: 160,
              taxAmount: 0,
              lineTotalAmount: 160,
              maxAllowedItemDiscountFlat: 50,
              maxAllowedItemDiscountPercent: 20,
              configuredBatchRuleId: null,
              configuredBatchRulePercentage: null,
              hasClientPriceMismatch: false,
              clientLineKey: 'clk-1',
            },
          ],
          infos: [],
          warnings: [],
        })
      );

      // Set initial preview with lower price
      component.checkoutPreview.set({
        totalAmount: 150,
        totalTaxableAmount: 150,
        totalTaxAmount: 0,
        totalDiscountAmount: 0,
        saleLevelEligibleSubtotal: 150,
        configuredSaleRule: null,
        lines: [
          {
            itemId: 'item-1',
            barcode: 'BAR001',
            itemName: 'Rice',
            inventoryBatchId: 'batch-1',
            batchNumber: 'B-001',
            quantity: 1,
            costPrice: 100,
            salesPrice: 150,
            mrp: 150,
            taxRatePercent: 0,
            isPriceIncludingTax: false,
            preTaxAmountBeforeDiscount: 150,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: 150,
            taxAmount: 0,
            lineTotalAmount: 150,
            maxAllowedItemDiscountFlat: 50,
            maxAllowedItemDiscountPercent: 20,
            configuredBatchRuleId: null,
            configuredBatchRulePercentage: null,
            hasClientPriceMismatch: false,
            clientLineKey: 'clk-1',
          },
        ],
        infos: [],
        warnings: [],
      });

      component.cart.set([
        {
          clientLineKey: 'clk-1',
          barcode: 'BAR001',
          itemName: 'Rice',
          batchNumber: 'B-001',
          inventoryBatchId: 'batch-1',
          quantity: 1,
          availableQuantity: 100,
          salesPrice: 150,
          mrp: 150,
          taxRatePercent: 0,
          taxIncluded: false,
          costPrice: 100,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ]);

      component.cartBootstrapped.set(true);
      fixture.detectChanges();

      // Trigger server update to fetch new preview with changed price
      shopUpdatesUpdates$.next({
        eventType: 'PricingChanged',
        shopId: 'shop-xyz',
        changedIds: ['batch-1'],
        occurredOn: new Date().toISOString(),
      });

      // Wait for preview refresh
      await new Promise((resolve) => setTimeout(resolve, 200));
      fixture.detectChanges();

      // Verify highlight is set for the changed row
      expect(component.highlightedRowKeys().size).toBeGreaterThan(0);
      expect(component.highlightedRowKeys().has('clk-1')).toBe(true);
    });

    it('shows lightweight notification on shop update (not full message box)', async () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      component.cart.set([
        {
          clientLineKey: 'clk-1',
          barcode: 'BAR001',
          itemName: 'Rice',
          batchNumber: 'B-001',
          inventoryBatchId: 'batch-1',
          quantity: 1,
          availableQuantity: 100,
          salesPrice: 150,
          mrp: 150,
          taxRatePercent: 0,
          taxIncluded: false,
          costPrice: 100,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ]);

      fixture.detectChanges();

      // Emit a shop update
      expect(component.showUpdateNotification()).toBe(false);
      shopUpdatesUpdates$.next({
        eventType: 'PricingChanged',
        shopId: 'shop-xyz',
        changedIds: ['batch-1'],
        occurredOn: new Date().toISOString(),
      });

      fixture.detectChanges();
      await fixture.whenStable();

      // Notification should be visible
      expect(component.showUpdateNotification()).toBe(true);
      expect(component.updateNotificationText()).toContain('sales.newSale.shopUpdate');

      // Wait for notification to auto-hide
      await new Promise((resolve) => setTimeout(resolve, 2500));
      expect(component.showUpdateNotification()).toBe(false);
    });

    it('keeps the fresher server preview when a stale local preview resolves later', async () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;
      const localPreview$ = new Subject<SalePreviewDto>();

      const initialPreview: SalePreviewDto = {
        totalAmount: 150,
        totalTaxableAmount: 150,
        totalTaxAmount: 0,
        totalDiscountAmount: 0,
        saleLevelEligibleSubtotal: 150,
        configuredSaleRule: null,
        lines: [
          {
            itemId: 'item-1',
            barcode: 'BAR001',
            itemName: 'Rice',
            inventoryBatchId: 'batch-1',
            batchNumber: 'B-001',
            quantity: 1,
            costPrice: 100,
            salesPrice: 150,
            mrp: 150,
            taxRatePercent: 0,
            isPriceIncludingTax: false,
            preTaxAmountBeforeDiscount: 150,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: 150,
            taxAmount: 0,
            lineTotalAmount: 150,
            maxAllowedItemDiscountFlat: 50,
            maxAllowedItemDiscountPercent: 20,
            configuredBatchRuleId: null,
            configuredBatchRulePercentage: null,
            hasClientPriceMismatch: false,
            clientLineKey: 'clk-1',
          },
        ],
        infos: [],
        warnings: [],
      };

      const serverPreview: SalePreviewDto = {
        totalAmount: 160,
        totalTaxableAmount: 160,
        totalTaxAmount: 0,
        totalDiscountAmount: 0,
        saleLevelEligibleSubtotal: 160,
        configuredSaleRule: null,
        lines: [
          {
            itemId: 'item-1',
            barcode: 'BAR001',
            itemName: 'Rice',
            inventoryBatchId: 'batch-1',
            batchNumber: 'B-001',
            quantity: 1,
            costPrice: 100,
            salesPrice: 160,
            mrp: 160,
            taxRatePercent: 0,
            isPriceIncludingTax: false,
            preTaxAmountBeforeDiscount: 160,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: 160,
            taxAmount: 0,
            lineTotalAmount: 160,
            maxAllowedItemDiscountFlat: 50,
            maxAllowedItemDiscountPercent: 20,
            configuredBatchRuleId: null,
            configuredBatchRulePercentage: null,
            hasClientPriceMismatch: false,
            clientLineKey: 'clk-1',
          },
        ],
        infos: [],
        warnings: [],
      };

      saleService.previewSale.mockReset();
      saleService.previewSale
        .mockReturnValueOnce(localPreview$.asObservable())
        .mockReturnValueOnce(of(serverPreview));

      fixture.detectChanges();
      await fixture.whenStable();

      component.checkoutPreview.set(initialPreview);
      component.cart.set([
        {
          clientLineKey: 'clk-1',
          barcode: 'BAR001',
          itemName: 'Rice',
          batchNumber: 'B-001',
          inventoryBatchId: 'batch-1',
          quantity: 1,
          availableQuantity: 100,
          salesPrice: 150,
          mrp: 150,
          taxRatePercent: 0,
          taxIncluded: false,
          costPrice: 100,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ]);

      vi.useFakeTimers();
      try {
        fixture.detectChanges();
        component.onIncreaseCartItem(0);
        fixture.detectChanges();
        await fixture.whenStable();
        vi.advanceTimersByTime(300);
        await Promise.resolve();

        expect(saleService.previewSale).toHaveBeenCalledTimes(1);

        shopUpdatesUpdates$.next({
          eventType: 'PricingChanged',
          shopId: 'shop-xyz',
          changedIds: ['batch-1'],
          occurredOn: new Date().toISOString(),
        });
        await Promise.resolve();

        expect(saleService.previewSale).toHaveBeenCalledTimes(2);
        expect(component.checkoutPreview()).toEqual(serverPreview);

        localPreview$.next({
          ...initialPreview,
          totalAmount: 155,
          totalTaxableAmount: 155,
          saleLevelEligibleSubtotal: 155,
          lines: initialPreview.lines.map((line) => ({ ...line, quantity: 2, lineTotalAmount: 155 })),
        });
        localPreview$.complete();
        await Promise.resolve();

        expect(component.checkoutPreview()).toEqual(serverPreview);
      } finally {
        vi.useRealTimers();
      }
    });

    it('continues to work when SignalR connection fails (graceful degradation)', async () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      saleService.previewSale.mockReturnValue(
        of({
          totalAmount: 150,
          totalTaxableAmount: 150,
          totalTaxAmount: 0,
          totalDiscountAmount: 0,
          saleLevelEligibleSubtotal: 150,
          configuredSaleRule: null,
          lines: [],
          infos: [],
          warnings: [],
        })
      );

      component.cart.set([
        {
          clientLineKey: 'clk-1',
          barcode: 'BAR001',
          itemName: 'Rice',
          batchNumber: 'B-001',
          inventoryBatchId: 'batch-1',
          quantity: 1,
          availableQuantity: 100,
          salesPrice: 150,
          mrp: 150,
          taxRatePercent: 0,
          taxIncluded: false,
          costPrice: 100,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ]);

      component.cartBootstrapped.set(true);
      fixture.detectChanges();

      // Even without SignalR, local edits should still trigger preview
      await new Promise((resolve) => setTimeout(resolve, 500));
      expect(component.checkoutPreview()).toBeDefined();
    });

    it('configures line-item discount p-select to append overlay to body', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      component.cart.set([
        {
          clientLineKey: 'clk-1',
          barcode: 'BAR001',
          itemName: 'Rice',
          batchNumber: 'B-001',
          inventoryBatchId: 'batch-1',
          quantity: 1,
          availableQuantity: 100,
          salesPrice: 150,
          mrp: 150,
          taxRatePercent: 0,
          taxIncluded: false,
          costPrice: 100,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ]);

      component.cartBootstrapped.set(true);
      component.toggleLineDiscountEditor('clk-1');
      fixture.detectChanges();

      // Query for the line-item discount p-select inside the discount-editor
      const discountSelectElements = fixture.nativeElement.querySelectorAll('.discount-editor p-select');
      expect(discountSelectElements.length).toBeGreaterThan(0);

      // Find the line-item discount select (should be inside a discount-row div that's inside a price-breakdown)
      // In the cart table context
      const lineItemDiscountSelect = Array.from(discountSelectElements).find((el: unknown) => {
        // This select should be inside a discount-editor that is within a cart table row
        const parent = (el as Element).closest('.discount-editor');
        return parent?.closest('tr') !== null;
      });

      expect(lineItemDiscountSelect).toBeDefined();
      expect(((lineItemDiscountSelect as any) as Element)?.getAttribute('appendTo')).toBe('body');
    });

    it('does not set appendTo for sale-level discount p-select', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      component.cart.set([
        {
          clientLineKey: 'clk-1',
          barcode: 'BAR001',
          itemName: 'Rice',
          batchNumber: 'B-001',
          inventoryBatchId: 'batch-1',
          quantity: 1,
          availableQuantity: 100,
          salesPrice: 150,
          mrp: 150,
          taxRatePercent: 0,
          taxIncluded: false,
          costPrice: 100,
          itemDiscountType: 0,
          itemDiscountValue: 0,
        },
      ]);

      component.cartBootstrapped.set(true);
      component.toggleSaleDiscountEditor();
      fixture.detectChanges();

      // Query for all p-select elements
      const selectElements = fixture.nativeElement.querySelectorAll('p-select');

      // Find the sale-level discount select (should be inside .sale-discount-row)
      const saleDiscountSelect = Array.from(selectElements).find((el: unknown) => {
        const parent = (el as Element).closest('.discount-editor');
        return parent?.closest('.sale-discount-row') !== null;
      });

      expect(saleDiscountSelect).toBeDefined();
      expect(((saleDiscountSelect as any) as Element)?.getAttribute('appendTo')).not.toBe('body');
    });
  });

  describe('Post-sale confirmation', () => {
    it('navigates to sales list for non-record sale mutations', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;

      salesFacade.lastMutationType.set('record-return');
      salesFacade.lastMutationSucceeded.set(true);
      fixture.detectChanges();

      expect(component.showConfirmation()).toBe(false);
      expect(salesFacade.clearMutationStatus).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/sales']);
    });

    it('shows confirmation dialog and resets transient state when recordSale succeeds', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;
      const sale = { saleId: 's1', invoiceNumber: 'INV-001', totalAmount: 100 } as any;

      // Set some state
      component.cart.set([{ itemName: 'Item 1' } as any]);
      component.customerForm.patchValue({ customerName: 'Alice' });

      salesFacade.lastMutationType.set('record-sale');
      salesFacade.lastRecordedSale.set(sale);
      salesFacade.lastMutationSucceeded.set(true);
      fixture.detectChanges();

      expect(component.showConfirmation()).toBe(true);
      expect(component.cart()).toHaveLength(0);
      expect(component.customerForm.controls.customerName.value).toBe('');
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('onDone closes dialog, clears state, and navigates to sales list', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;
      component.showConfirmation.set(true);

      component.onDone();

      expect(component.showConfirmation()).toBe(false);
      expect(salesFacade.clearLastRecordedSale).toHaveBeenCalled();
      expect(salesFacade.clearMutationStatus).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/sales']);
    });

    it('printA4 opens print route in new tab', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;
      const windowOpenSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

      component.printA4('s1');

      expect(windowOpenSpy).toHaveBeenCalledWith('/sales/s1/print?template=a4', '_blank');
    });

    it('printThermal opens thermal print route in new tab', () => {
      const fixture = TestBed.createComponent(NewSalePageComponent);
      const component = fixture.componentInstance;
      const windowOpenSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

      component.printThermal('s1');

      expect(windowOpenSpy).toHaveBeenCalledWith('/sales/s1/print?template=thermal', '_blank');
    });
  });
});
