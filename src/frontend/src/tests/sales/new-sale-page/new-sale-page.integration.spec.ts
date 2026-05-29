import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: integration', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('starts shop updates connection on init', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();

    expect(deps.shopUpdatesService.startConnection).toHaveBeenCalledTimes(1);
  });

  it('renders the POS shell regions and hides out-of-scope controls', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const forbiddenKeys = [
      'sales.newSale.holdSale',
      'sales.newSale.manualOffline',
      'sales.newSale.manualOfflineToggle',
      'sales.newSale.saleNumber',
      'sales.newSale.invoiceNumber',
    ];

    expect(root.querySelector('.new-sale-header')).not.toBeNull();
    expect(root.querySelector('.new-sale-workspace')).not.toBeNull();
    expect(root.querySelector('.new-sale-main')).not.toBeNull();
    expect(root.querySelector('.new-sale-checkout-panel')).not.toBeNull();
    expect(root.querySelector('.new-sale-product-lookup')).not.toBeNull();
    expect(root.querySelector('.new-sale-cart')).not.toBeNull();
    expect(root.querySelector('.new-sale-checkout-section')).not.toBeNull();
    const headerActions = root.querySelector('.new-sale-header-actions');
    expect(headerActions).not.toBeNull();
    expect(headerActions!.querySelectorAll('button')).toHaveLength(1);
    forbiddenKeys.forEach((key) => {
      expect(root.textContent).not.toContain(key);
    });
  });

  it('navigates away on cancel', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.onCancel();
    expect(deps.router.navigate).toHaveBeenCalledWith(['/sales']);
  });

  it('shows the offline status chip only in offline mode', () => {
    TestBed.resetTestingModule();
    setupNewSalePageTestBed({
      networkStatus: {
        isOnline: signal(false),
        canReachApi: signal(false),
        lastVerifiedAt: signal<Date | null>(null),
        isChecking: signal(false),
        checkConnectivity: vi.fn(async () => undefined),
      },
    }).deps;

    const offlineFixture = TestBed.createComponent(NewSalePageComponent);
    offlineFixture.detectChanges();

    expect((offlineFixture.nativeElement as HTMLElement).textContent).toContain('sales.newSale.offlineStatusChip');

    TestBed.resetTestingModule();
    setupNewSalePageTestBed();

    const onlineFixture = TestBed.createComponent(NewSalePageComponent);
    onlineFixture.detectChanges();

    expect((onlineFixture.nativeElement as HTMLElement).textContent).not.toContain('sales.newSale.offlineStatusChip');
  });

  it('renders grouped Goods/Services sections only for mixed carts', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.cart.set([
      {
        clientLineKey: 'clk-g-1',
        barcode: 'A',
        itemName: 'Item',
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
        hsnCode: null,
      },
    ]);

    vm.serviceCart.set([
      {
        kind: 'service',
        clientLineKey: 'clk-s-1',
        serviceId: 'svc-1',
        serviceName: 'Bike wash',
        serviceCode: 'S-001',
        quantity: 1,
        unitPrice: 100,
        taxRatePercent: 0,
        taxIncluded: false,
        hsnCode: null,
      },
    ]);

    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('en.sales.newSale.goodsSection');
    expect(root.textContent).toContain('en.sales.newSale.servicesSection');
    expect(root.querySelectorAll('tfoot tr').length).toBeGreaterThan(0);

    vm.serviceCart.set([]);
    fixture.detectChanges();
    expect(root.textContent).not.toContain('en.sales.newSale.servicesSection');
  });
});
