import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopService } from '../../shops/services/shop.service';
import { PurchaseOrderDetail } from '../services/purchase-order.service';
import { PurchaseOrderService } from '../services/purchase-order.service';
import { PurchaseOrderPrintViewComponent } from './purchase-order-print-view.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8') as string) as Record<string, unknown>;

const order: PurchaseOrderDetail = {
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: 'Placed',
  supplierId: 'supplier-1',
  supplierName: 'Acme Supplies',
  supplierReference: 'ACME-42',
  receivedQuantity: 0,
  orderDate: '2026-06-01',
  expectedDeliveryDate: '2026-06-10',
  supplierReferenceNumber: 'SUP-REF-9',
  notes: 'Deliver before noon.',
  lines: [
    {
      lineId: 'line-1',
      itemId: 'item-1',
      description: 'Widget A',
      expectedQuantity: 3,
      unitCost: 50,
      lineTotal: 150,
    },
    {
      lineId: 'line-2',
      itemId: 'item-2',
      description: 'Widget B',
      expectedQuantity: 2,
      unitCost: 25,
      lineTotal: 50,
    },
  ],
  expectedTotal: 200,
  createdAt: '2026-06-01T08:00:00Z',
  cancellationReason: null,
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

describe('PurchaseOrderPrintViewComponent', () => {
  const purchaseOrderService = {
    getPurchaseOrderDetail: vi.fn(),
  };
  const shopService = {
    getShopDetails: vi.fn(),
  };
  const authService = {
    session: vi.fn(),
  };

  const createComponent = (): ComponentFixture<PurchaseOrderPrintViewComponent> => {
    TestBed.configureTestingModule({
      imports: [
        CommonModule,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
        PurchaseOrderPrintViewComponent,
      ],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ purchaseOrderId: 'po-1' }) } },
        },
        { provide: PurchaseOrderService, useValue: purchaseOrderService },
        { provide: ShopService, useValue: shopService },
        { provide: AuthService, useValue: authService },
      ],
    });

    return TestBed.createComponent(PurchaseOrderPrintViewComponent);
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(window, 'print').mockImplementation(() => undefined);
    purchaseOrderService.getPurchaseOrderDetail.mockReset();
    shopService.getShopDetails.mockReset();
    authService.session.mockReset();
    purchaseOrderService.getPurchaseOrderDetail.mockReturnValue(of(order));
    shopService.getShopDetails.mockReturnValue(of(shop));
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

  it('renders supplier, PO context, dates, lines, quantities, costs, totals, and notes', () => {
    const fixture = createComponent();
    fixture.detectChanges();
    vi.runOnlyPendingTimers();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Main Shop');
    expect(host.textContent).toContain('Acme Supplies');
    expect(host.textContent).toContain('PO-2026-000001');
    expect(host.textContent).toContain('Placed');
    expect(host.textContent).toContain('2026-06-01');
    expect(host.textContent).toContain('2026-06-10');
    expect(host.textContent).toContain('SUP-REF-9');
    expect(host.textContent).toContain('Deliver before noon.');
    expect(host.textContent).toContain('Widget A');
    expect(host.textContent).toContain('Widget B');
    expect(host.textContent).toContain('3');
    expect(host.textContent).toContain('50.00');
    expect(host.textContent).toContain('150.00');
    expect(host.textContent).toContain('200.00');
    expect(window.print).toHaveBeenCalledTimes(1);
  });
});
