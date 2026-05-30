import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { SaleInvoiceThermalComponent } from './sale-invoice-thermal.component';
import type { SaleDto, SaleItemDto, SaleReturnDto } from '../services/sale.models';
import { ShopDetails } from '../../shops/services/shop.service';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8') as string) as Record<string, unknown>;

describe('SaleInvoiceThermalComponent', () => {
  const makeSaleItem = (overrides: Partial<SaleItemDto> = {}): SaleItemDto => ({
    saleItemId: 'item-1',
    lineType: 'Goods',
    itemId: 'item-1',
    serviceId: null,
    itemName: 'Test Item',
    lineCode: 'ITEM-001',
    inventoryBatchId: 'batch-1',
    quantity: 2,
    salesPrice: 100,
    originalSalesPrice: 100,
    finalSalesPrice: 100,
    preTaxAmountBeforeDiscount: 200,
    itemDiscountAmount: 0,
    saleDiscountAmount: 0,
    taxableAmount: 200,
    taxAmount: 36,
    totalAmount: 236,
    savingsAmount: 0,
    taxRatePercent: 18,
    isPriceIncludingTax: false,
    hasPriceMismatch: false,
    hsnCode: null,
    returnedQuantity: 0,
    returnableQuantity: 2,
    returnStatus: 'Returnable',
    ...overrides,
  });

  const makeSaleReturn = (overrides: Partial<SaleReturnDto> = {}): SaleReturnDto => ({
    saleReturnId: 'return-1',
    returnNumber: 'RET-001',
    returnedAt: '2026-05-10T10:00:00Z',
    totalRefundAmount: 40,
    dueReductionAmount: 0,
    payoutAmount: 0,
    isVoided: false,
    voidedAt: null,
    voidReason: null,
    items: [],
    ...overrides,
  });

  const makeSale = (overrides: Partial<SaleDto> = {}): SaleDto => ({
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    customerId: 'customer-1',
    customerName: 'John Doe',
    customerPhone: '+919999111222',
    paymentMethod: 1,
    soldAt: '2026-05-01T10:00:00Z',
    paidAmount: 0,
    dueAmount: 0,
    totalBeforeDiscount: 100,
    totalDiscountAmount: 5,
    totalAmount: 95,
    totalTaxAmount: 12,
    items: [makeSaleItem()],
    returns: [],
    warnings: [],
    ...overrides,
  });

  const makeShop = (overrides: Partial<ShopDetails> = {}): ShopDetails => ({
    shopId: 'shop-1',
    name: 'Test Shop',
    address: '123 Market Street',
    city: 'Mumbai',
    state: 'MH',
    pincode: '400001',
    contactPerson: null,
    mobileNumber: '+919876543210',
    gstNumber: 'GSTIN123456789',
    bankName: null,
    bankAccountNumber: null,
    bankAccountType: null,
    ifscCode: null,
    accountHolderName: null,
    ...overrides,
  });

  let component: SaleInvoiceThermalComponent;
  let fixture: ComponentFixture<SaleInvoiceThermalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        CommonModule,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
        SaleInvoiceThermalComponent,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SaleInvoiceThermalComponent);
    component = fixture.componentInstance;
  });

  it('renders compact shop and invoice header', () => {
    const sale = makeSale();
    const shop = makeShop({
      name: 'Acme Retail',
      city: 'Pune',
      state: 'MH',
      pincode: '411001',
    });

    component.sale = sale;
    component.shop = shop;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent ?? '';
    expect(text).toContain('Acme Retail');
    expect(text).toContain('123 Market Street');
    expect(text).toContain('Pune');
    expect(text).toContain('MH');
    expect(text).toContain('411001');
    expect(text).toContain('GSTIN123456789');
    expect(text).toContain('INV-001');
    expect(text).toContain('Invoice');
  });

  it('shows pending sync marker when enabled', () => {
    component.sale = makeSale();
    component.shop = makeShop();
    component.pendingSync = true;
    fixture.detectChanges();

    const marker = fixture.nativeElement.querySelector('.thermal-invoice__pending-text');
    expect(marker).not.toBeNull();
    expect(marker?.textContent).toContain('Sale Queued for Sync');
  });

  it('does not show pending sync marker when disabled', () => {
    component.sale = makeSale();
    component.shop = makeShop();
    fixture.detectChanges();

    const marker = fixture.nativeElement.querySelector('.thermal-invoice__pending-text');
    expect(marker).toBeNull();
  });

  it('renders Walk-in customer fallback', () => {
    const sale = makeSale({ customerId: null, customerName: null, customerPhone: null });

    component.sale = sale;
    component.shop = makeShop();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Walk-in');
    expect(fixture.nativeElement.textContent).not.toContain('+919999111222');
  });

  it('renders customer name and phone', () => {
    const sale = makeSale({
      customerId: null,
      customerName: 'Jane Doe',
      customerPhone: '+919888777777',
    });

    component.sale = sale;
    component.shop = makeShop();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent ?? '';
    expect(text).toContain('Jane Doe');
    expect(text).toContain('+919888777777');
  });

  it('renders item lines with quantity, tax, and line total', () => {
    const sale = makeSale({
      items: [
        makeSaleItem({
          itemName: 'Rice 5kg',
          quantity: 2,
          salesPrice: 50,
          taxRatePercent: 5,
          taxAmount: 5,
          totalAmount: 105,
          itemDiscountAmount: 10,
        }),
      ],
    });

    component.sale = sale;
    component.shop = makeShop();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent ?? '';
    expect(text).toContain('Rice 5kg');
    expect(text).toContain('2 x ₹50.00');
    expect(text).toContain('Tax 5%: ₹5.00');
    expect(text).toContain('Discount: -₹10.00');
    expect(text).toContain('₹105.00');
  });

  it('groups goods and services for mixed thermal invoices', () => {
    component.sale = makeSale({
      items: [
        makeSaleItem({ lineType: 'Goods', itemName: 'Engine Oil', hsnCode: '2710' }),
        makeSaleItem({ lineType: 'Service', itemName: 'Bike Wash', hsnCode: '9987' }),
      ],
    });
    component.shop = makeShop();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent ?? '';
    expect(text).toContain('sales.newSale.cart.goodsSection');
    expect(text).toContain('sales.newSale.cart.servicesSection');
    expect(text).toContain('HSN: 2710');
    expect(text).toContain('SAC: 9987');
  });

  it('keeps service-only thermal invoice flat and labels SAC', () => {
    component.sale = makeSale({
      items: [
        makeSaleItem({
          lineType: 'Service',
          itemName: 'Bike Wash',
          hsnCode: '9987',
        }),
      ],
    });
    component.shop = makeShop();
    fixture.detectChanges();

    const groupHeaders = fixture.nativeElement.querySelectorAll('.thermal-invoice__group-header');
    const text = fixture.nativeElement.textContent ?? '';
    expect(groupHeaders.length).toBe(0);
    expect(text).toContain('SAC: 9987');
    expect(text).not.toContain('HSN:');
  });

  it('renders payment summary and partial payment status', () => {
    const sale = makeSale({
      paidAmount: 40,
      dueAmount: 55,
      totalBeforeDiscount: 150,
      totalDiscountAmount: 10,
      totalTaxAmount: 8,
      totalAmount: 148,
    });

    component.sale = sale;
    component.shop = makeShop();
    fixture.detectChanges();

    const summary = fixture.nativeElement.querySelector('.thermal-invoice');
    const rows = summary?.querySelectorAll('.thermal-invoice__payment-row') ?? [];

    expect(rows.length).toBe(4);
    expect(summary?.textContent).toContain('Paid');
    expect(summary?.textContent).toContain('Due');
    expect(summary?.textContent).toContain('₹40.00');
    expect(summary?.textContent).toContain('₹55.00');
    expect(summary?.textContent).toContain('Partially paid');
    expect(summary?.textContent).toContain('Subtotal');
  });

  it('renders returns only when present', () => {
    const sale = makeSale({
      returns: [
        makeSaleReturn({
          returnNumber: 'RET-900',
          returnedAt: '2026-05-11T12:00:00Z',
          totalRefundAmount: 30,
          isVoided: true,
          voidReason: 'Damaged item',
        }),
      ],
    });

    component.sale = sale;
    component.shop = makeShop();
    fixture.detectChanges();

    const returnSection = fixture.nativeElement.querySelector('.thermal-invoice__returns');
    expect(returnSection).not.toBeNull();
    expect(returnSection.textContent).toContain('RET-900');
    expect(returnSection.textContent).toContain('11/05/2026');
    expect(returnSection.textContent).toContain('₹30.00');
    expect(returnSection?.textContent).toContain('Voided');
    expect(returnSection?.textContent).toContain('Void Reason: Damaged item');
  });

  it('omits returns section when empty', () => {
    const sale = makeSale({ returns: [] });

    component.sale = sale;
    component.shop = makeShop();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.thermal-invoice__returns')).toBeNull();
  });

  it('does not render cost, profit, or margin fields', () => {
    component.sale = makeSale({
      items: [
        makeSaleItem({
          returnStatus: 'Returnable',
          returnableQuantity: 0,
        }),
      ],
    });
    component.shop = makeShop();
    fixture.detectChanges();

    const text = (fixture.nativeElement.textContent ?? '').toLowerCase();
    expect(text).not.toContain('cost');
    expect(text).not.toContain('profit');
    expect(text).not.toContain('margin');
  });

  describe('GST/HSN conditional rendering', () => {
    it('GST shop: shows GSTIN in header', () => {
      component.sale = makeSale();
      component.shop = makeShop({ gstNumber: 'GSTIN99887766' });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('GSTIN99887766');
    });

    it('non-GST shop: hides GSTIN in header', () => {
      component.sale = makeSale();
      component.shop = makeShop({ gstNumber: null });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain('GSTIN');
    });

    it('GST shop: shows HSN label per item line', () => {
      component.sale = makeSale({ items: [makeSaleItem({ hsnCode: '5602' })] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('HSN');
      expect(fixture.nativeElement.textContent).toContain('5602');
    });

    it('GST shop: blank HSN renders blank, not N/A', () => {
      component.sale = makeSale({ items: [makeSaleItem({ hsnCode: null })] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain('N/A');
    });

    it('non-GST shop: hides HSN line per item', () => {
      component.sale = makeSale({ items: [makeSaleItem({ hsnCode: '6303' })] });
      component.shop = makeShop({ gstNumber: null });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain('HSN');
      expect(fixture.nativeElement.textContent).not.toContain('6303');
    });

    it('non-GST shop: tax line still visible', () => {
      component.sale = makeSale({ items: [makeSaleItem({ taxRatePercent: 18, taxAmount: 36 })] });
      component.shop = makeShop({ gstNumber: null });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Tax 18%');
    });
  });
});
