import '@angular/compiler';
import { Injectable, signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { EMPTY } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { NetworkStatusService } from '../../../core/services/network-status.service';
import { ProductCatalogSyncService } from '../../../core/services/product-catalog-sync.service';
import { ShopUpdatesSignalRService } from '../../../core/services/shop-updates-signalr.service';
import { Customer } from '../../customers/services/customer.service';
import { CustomersFacade } from '../../customers/state/customers.facade';
import { InventoryService } from '../../inventory/services/inventory.service';
import { OfflineSaleStateService } from '../services/offline-sale-state.service';
import { SaleCartStateService } from '../services/sale-cart-state.service';
import { SalePreviewService } from '../services/sale-preview.service';
import { SaleService } from '../services/sale.service';
import { SalesFacade } from '../state/sales.facade';
import { NewSalePageSearchCustomerService } from './new-sale-page.search-customer.service';

function customer(customerId: string, isActive = true): Customer {
  return {
    customerId,
    name: customerId === 'c1' ? 'Alice Cooper' : 'Gamma Corner',
    phoneNumber: customerId === 'c1' ? '9999999999' : '7777777777',
    address: 'Main Street',
    isActive,
    creditLimit: 500,
    purchaseCount: 1,
    lifetimeRevenue: 100,
    currentMonthRevenue: 25,
    outstandingDue: isActive ? 50 : 0,
  };
}

function customerDto(customerId: string) {
  return {
    customerId,
    name: 'Alice Cooper',
    phoneNumber: '9999999999',
    address: 'Main Street',
  };
}

@Injectable()
class TestNewSalePageSearchCustomerService extends NewSalePageSearchCustomerService {
  applyRouteCustomerPreselection(): void {
    this.preselectRouteCustomerIfPossible();
  }

  override onAddToCart(): void {}
  override onIncreaseCartItem(): void {}
  override onDecreaseCartItem(): void {}
  override onRemoveCartItem(): void {}
  override onRemoveServiceItem(): void {}
  override onServiceItemPriceChange(): void {}
  override onServiceItemQuantityChange(): void {}
  override onQuickProductTileSelected(): void {}
  override canIncreaseCartItem(): boolean {
    return false;
  }
  override hasTax(): boolean {
    return false;
  }
  override getLineSubtotal(): number {
    return 0;
  }
  override getLineTaxAmount(): number {
    return 0;
  }
  override getLineTotal(): number {
    return 0;
  }
  override getUnitSubtotal(): number {
    return 0;
  }
  override getUnitTaxAmount(): number {
    return 0;
  }
  override getUnitFinalPrice(): number {
    return 0;
  }
  override async onSubmit(): Promise<void> {}
  override createSaleIdempotencyKey(): string {
    return 'test-key';
  }
  override onCancel(): void {}
  override toggleSaleDiscountEditor(): void {}
  override onSaleDiscountTypeChange(): void {}
  override onSaleDiscountValueChange(): void {}
  override isSaleDiscountEligible(): boolean {
    return false;
  }
  override getPreviewLine(): null {
    return null;
  }
  override toggleLineDiscountEditor(): void {}
  override isLineDiscountEditorOpen(): boolean {
    return false;
  }
  override getCartItemDiscountError(): string {
    return '';
  }
  override getCartItemHsnError(): string {
    return '';
  }
  override getCartItemTaxError(): string {
    return '';
  }
  override getPaymentMethodLabel(): any {
    return 'Cash';
  }
  override onCartItemDiscountTypeChange(): void {}
  override onCartItemDiscountValueChange(): void {}
  override onCartItemHsnCodeChange(): void {}
  override onCartItemTaxRateChange(): void {}
  override getLineDiscountLimits(): { maxFlat: number; maxPercent: number } {
    return { maxFlat: 0, maxPercent: 0 };
  }
  override getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number } {
    return { isEligible: false, maxFlat: 0, maxPercent: 0 };
  }
  override revalidateDiscountsAgainstPreview(): void {}
  override revalidateLineOverrides(): boolean {
    return false;
  }
  override getBlockingCartValidationErrorKey(): string | null {
    return null;
  }
  override normalizeHsnCode(): string | null {
    return null;
  }
  override normalizeAmount(): number {
    return 0;
  }
  override toFiniteAmount(): number {
    return 0;
  }
  override roundAmount(value: number): number {
    return value;
  }
  override areAmountsEqual(left: number, right: number): boolean {
    return left === right;
  }
  override syncPaymentSplitFromPaid(): void {}
  override syncPaymentSplitFromDue(): void {}
  override enforceNoCustomerCreditRestrictions(): void {}
  override resetSearchAndPickerState(): void {}
  override applyProductDefaultsForLine(): void {}
  override getEventNotificationKey(): string {
    return 'test';
  }
  override detectAndHighlightChangedRows(): void {}
  override resetTransientState(): void {}
  override async searchOfflineCatalog(): Promise<void> {}
  override async refreshOfflinePreview(): Promise<void> {}
  override async onOfflineSubmit(): Promise<void> {}
  override printA4(): void {}
  override printThermal(): void {}
  override printOfflineA4(): void {}
  override printOfflineThermal(): void {}
}

describe('NewSalePageSearchCustomerService', () => {
  function buildService(routeCustomerId: string | null) {
    TestBed.resetTestingModule();

    const customers = signal<Customer[]>([]);
    const loadingCustomers = signal(true);
    const router = { navigate: vi.fn().mockResolvedValue(true) };
    const route = {
      snapshot: {
        queryParamMap: convertToParamMap(routeCustomerId ? { customerId: routeCustomerId } : {}),
      },
    };

    TestBed.configureTestingModule({
      imports: [ReactiveFormsModule],
      providers: [
        TestNewSalePageSearchCustomerService,
        { provide: ActivatedRoute, useValue: route },
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: { session: signal({ activeShopId: 'shop-1' }) } },
        { provide: NetworkStatusService, useValue: { canReachApi: vi.fn(() => true) } },
        { provide: ProductCatalogSyncService, useValue: { filterByName: vi.fn(() => []), filterByBarcode: vi.fn(() => []) } },
        { provide: ShopUpdatesSignalRService, useValue: { updates$: EMPTY, startConnection: vi.fn(), stopConnection: vi.fn() } },
        { provide: CustomersFacade, useValue: { allCustomers: customers, loadingCustomers } },
        { provide: InventoryService, useValue: {} },
        {
          provide: SalesFacade,
          useValue: {
            submitting: signal(false),
            errorMessage: signal(''),
            lastMutationSucceeded: signal(false),
            lastMutationType: signal(null),
            lastRecordedSale: signal(null),
            loadSales: vi.fn(),
            loadSaleDetail: vi.fn(),
            clearSaleDetail: vi.fn(),
          },
        },
        {
          provide: SaleCartStateService,
          useValue: {
            cart: signal([]),
            serviceCart: signal([]),
            cartBootstrapped: signal(false),
            getServiceLineSubtotal: vi.fn(() => 0),
            getServiceLineTaxAmount: vi.fn(() => 0),
            getServiceLineTotal: vi.fn(() => 0),
          },
        },
        {
          provide: SalePreviewService,
          useValue: {
            checkoutPreview: signal(null),
            isPreviewLoading: signal(false),
            previewError: signal(''),
            beginPreviewRequest: vi.fn(() => 1),
            finishPreviewRequest: vi.fn(),
            refreshOnlinePreview: vi.fn(),
            refreshOnServerUpdate: vi.fn(),
            clearPreviewState: vi.fn(),
            triggerServerUpdateRefresh: vi.fn(),
            clearPreviewError: vi.fn(),
            markPreviewInvalid: vi.fn(),
          },
        },
        {
          provide: OfflineSaleStateService,
          useValue: {
            offlineDeviceSettings: signal(null),
            snapshotCompletedAt: signal(null),
            offlinePendingCount: signal(0),
            offlineNeedsReviewCount: signal(0),
            offlineInvoiceRemaining: signal(0),
            offlineCatalog: signal([]),
            offlineCustomers: signal([]),
            searchOfflineCatalog: vi.fn(),
            buildOfflinePreview: vi.fn(),
            submitOfflineSale: vi.fn(),
            refreshSnapshot: vi.fn(),
          },
        },
        { provide: SaleService, useValue: {} },
      ],
    });

    const service = TestBed.inject(TestNewSalePageSearchCustomerService);
    return { customers, loadingCustomers, service };
  }

  it('preselects an active route customer after customers finish loading', () => {
    const activeCustomer = customer('c1', true);
    const { customers, loadingCustomers, service } = buildService('c1');
    const selectionSpy = vi.spyOn(service, 'onCustomerSectionSelected');

    customers.set([activeCustomer]);
    service.applyRouteCustomerPreselection();
    expect(selectionSpy).not.toHaveBeenCalled();

    loadingCustomers.set(false);
    service.applyRouteCustomerPreselection();

    expect(selectionSpy).toHaveBeenCalledWith(customerDto('c1'));
    expect(service.selectedCustomerId()).toBe('c1');
    expect(service.selectedCustomerName()).toBe('alice cooper');
  });

  it('ignores missing customerId', () => {
    const { customers, loadingCustomers, service } = buildService(null);
    const selectionSpy = vi.spyOn(service, 'onCustomerSectionSelected');

    customers.set([customer('c1', true)]);
    loadingCustomers.set(false);
    service.applyRouteCustomerPreselection();

    expect(selectionSpy).not.toHaveBeenCalled();
    expect(service.selectedCustomerId()).toBeNull();
  });

  it('ignores unknown and inactive customerIds', () => {
    const activeCustomer = customer('c1', true);
    const inactiveCustomer = customer('c2', false);
    const { customers, loadingCustomers, service } = buildService('c2');
    const selectionSpy = vi.spyOn(service, 'onCustomerSectionSelected');

    customers.set([activeCustomer, inactiveCustomer]);
    loadingCustomers.set(false);
    service.applyRouteCustomerPreselection();

    expect(selectionSpy).not.toHaveBeenCalled();
    expect(service.selectedCustomerId()).toBeNull();
  });
});
