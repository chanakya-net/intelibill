import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CommonModule } from '@angular/common';

import { SaleInvoiceThermalComponent } from './sale-invoice-thermal.component';
import { SaleDto, SaleItemDto, SaleReturnDto } from '../services/sale.service';
import { ShopDetails } from '../../shops/services/shop.service';

describe('SaleInvoiceThermalComponent', () => {
  const makeSaleItem = (overrides: Partial<SaleItemDto> = {}): SaleItemDto => ({
    saleItemId: 'item-1',
    itemId: 'item-1',
    itemName: 'Test Item',
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
      imports: [CommonModule, SaleInvoiceThermalComponent],
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
    expect(returnSection.textContent).toContain('VOIDED');
    expect(returnSection.textContent).toContain('Reason: Damaged item');
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
});
