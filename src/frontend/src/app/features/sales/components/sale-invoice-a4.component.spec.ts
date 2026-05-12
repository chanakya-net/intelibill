import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { SaleInvoiceA4Component } from './sale-invoice-a4.component';
import { SaleDto, SaleItemDto } from '../services/sale.service';
import { ShopDetails } from '../../shops/services/shop.service';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8') as string) as Record<string, unknown>;

describe('SaleInvoiceA4Component', () => {
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

  const makeSale = (overrides: Partial<SaleDto> = {}): SaleDto => ({
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    customerId: 'customer-1',
    customerName: 'John Doe',
    customerPhone: '+919999111222',
    paymentMethod: 1,
    soldAt: '2026-05-01T10:00:00Z',
    paidAmount: 100,
    dueAmount: 0,
    totalBeforeDiscount: 100,
    totalDiscountAmount: 0,
    totalAmount: 100,
    totalTaxAmount: 5,
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
    contactPerson: 'Owner Name',
    mobileNumber: '+919876543210',
    gstNumber: 'GSTIN123456789',
    bankName: null,
    bankAccountNumber: null,
    bankAccountType: null,
    ifscCode: null,
    accountHolderName: null,
    ...overrides,
  });

  let component: SaleInvoiceA4Component;
  let fixture: ComponentFixture<SaleInvoiceA4Component>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        CommonModule,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
        SaleInvoiceA4Component,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SaleInvoiceA4Component);
    component = fixture.componentInstance;
  });

  describe('invoice header', () => {
    it('renders shop name in header', () => {
      const sale = makeSale();
      const shop = makeShop({ name: 'Premium Shop' });

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).toContain('Premium Shop');
    });

    it('renders shop address with city, state and pincode', () => {
      const sale = makeSale();
      const shop = makeShop({
        address: '456 Main St',
        city: 'Delhi',
        state: 'DL',
        pincode: '110001',
      });

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).toContain('456 Main St');
      expect(headerElement?.textContent).toContain('Delhi');
      expect(headerElement?.textContent).toContain('DL');
      expect(headerElement?.textContent).toContain('110001');
    });

    it('renders shop mobile number when present', () => {
      const sale = makeSale();
      const shop = makeShop({ mobileNumber: '+919123456789' });

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).toContain('+919123456789');
    });

    it('does not render mobile number when absent', () => {
      const sale = makeSale();
      const shop = makeShop({ mobileNumber: null });

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      const mobileText = headerElement?.textContent.match(/\+91\d+/) || [];
      expect(mobileText.length).toBe(0);
    });

    it('renders shop GST number when present', () => {
      const sale = makeSale();
      const shop = makeShop({ gstNumber: 'GSTIN987654321' });

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).toContain('GSTIN987654321');
    });

    it('does not render GST number when absent', () => {
      const sale = makeSale();
      const shop = makeShop({ gstNumber: null });

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).not.toContain('GSTIN');
    });

    it('renders invoice number and date', () => {
      const sale = makeSale({
        invoiceNumber: 'INV-123456',
        soldAt: '2026-05-15T14:30:00Z',
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).toContain('INV-123456');
    });

    it('renders customer name', () => {
      const sale = makeSale({ customerName: 'Jane Smith' });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const customerElement = fixture.nativeElement.querySelector('.invoice__customer');
      expect(customerElement?.textContent).toContain('Jane Smith');
    });

    it('renders customer phone when present', () => {
      const sale = makeSale({ customerPhone: '+919988776655' });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const customerElement = fixture.nativeElement.querySelector('.invoice__customer');
      expect(customerElement?.textContent).toContain('+919988776655');
    });

    it('renders Walk-in fallback when customer name is null', () => {
      const sale = makeSale({ customerId: null, customerName: null, customerPhone: null });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const customerElement = fixture.nativeElement.querySelector('.invoice__customer');
      expect(customerElement?.textContent).toContain('Walk-in');
    });
  });

  describe('item table', () => {
    it('renders item name column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ itemName: 'Product A' })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('Product A');
    });

    it('renders quantity column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ quantity: 5 })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('5');
    });

    it('renders unit price column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ finalSalesPrice: 150, salesPrice: 150 })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('150');
    });

    it('renders discount column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ itemDiscountAmount: 20 })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toMatch(/20/);
    });

    it('renders taxable amount column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ taxableAmount: 300 })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toMatch(/300/);
    });

    it('renders tax rate column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ taxRatePercent: 18 })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('18%');
    });

    it('renders tax amount column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ taxAmount: 54 })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toMatch(/54/);
    });

    it('renders line total column', () => {
      const sale = makeSale({
        items: [makeSaleItem({ totalAmount: 354 })],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toMatch(/354/);
    });

    it('renders multiple items', () => {
      const sale = makeSale({
        items: [
          makeSaleItem({ itemName: 'Item 1' }),
          makeSaleItem({ itemName: 'Item 2' }),
          makeSaleItem({ itemName: 'Item 3' }),
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('Item 1');
      expect(tableElement?.textContent).toContain('Item 2');
      expect(tableElement?.textContent).toContain('Item 3');
    });

    it('does not render cost price field', () => {
      const sale = makeSale({
        items: [makeSaleItem()],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).not.toMatch(/cost price|cost|costprice/i);
    });

    it('does not render profit field', () => {
      const sale = makeSale({
        items: [makeSaleItem()],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).not.toMatch(/profit|margin/i);
    });
  });

  describe('payment summary', () => {
    it('renders total before discount', () => {
      const sale = makeSale({ totalBeforeDiscount: 500 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toContain('500');
    });

    it('renders discount amount', () => {
      const sale = makeSale({ totalDiscountAmount: 50 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toMatch(/50/);
    });

    it('renders tax amount', () => {
      const sale = makeSale({ totalTaxAmount: 90 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toMatch(/90/);
    });

    it('renders grand total', () => {
      const sale = makeSale({ totalAmount: 540 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toMatch(/540/);
    });

    it('renders payment mode', () => {
      const sale = makeSale({ paymentMethod: 1 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toContain('Cash');
    });

    it('renders paid amount', () => {
      const sale = makeSale({ paidAmount: 540 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toMatch(/540/);
    });

    it('renders due amount', () => {
      const sale = makeSale({ dueAmount: 100 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toMatch(/100/);
    });

    it('renders payment status as Paid when due is 0', () => {
      const sale = makeSale({ dueAmount: 0 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toContain('Paid');
    });

    it('renders payment status as Partial when due is greater than 0', () => {
      const sale = makeSale({ dueAmount: 50 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toContain('Partial');
    });
  });

  describe('returns section', () => {
    it('does not render returns section when no returns exist', () => {
      const sale = makeSale({ returns: [] });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement).toBeNull();
    });

    it('renders returns section when returns exist', () => {
      const sale = makeSale({
        returns: [
          {
            saleReturnId: 'return-1',
            returnNumber: 'RET-001',
            returnedAt: '2026-05-02T10:00:00Z',
            totalRefundAmount: 100,
            dueReductionAmount: 0,
            payoutAmount: 100,
            isVoided: false,
            voidedAt: null,
            voidReason: null,
            items: [],
          },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement).not.toBeNull();
    });

    it('renders return number', () => {
      const sale = makeSale({
        returns: [
          {
            saleReturnId: 'return-1',
            returnNumber: 'RET-12345',
            returnedAt: '2026-05-02T10:00:00Z',
            totalRefundAmount: 100,
            dueReductionAmount: 0,
            payoutAmount: 100,
            isVoided: false,
            voidedAt: null,
            voidReason: null,
            items: [],
          },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement?.textContent).toContain('RET-12345');
    });

    it('renders refund amount', () => {
      const sale = makeSale({
        returns: [
          {
            saleReturnId: 'return-1',
            returnNumber: 'RET-001',
            returnedAt: '2026-05-02T10:00:00Z',
            totalRefundAmount: 250,
            dueReductionAmount: 0,
            payoutAmount: 250,
            isVoided: false,
            voidedAt: null,
            voidReason: null,
            items: [],
          },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement?.textContent).toMatch(/250/);
    });

    it('renders void status when return is voided', () => {
      const sale = makeSale({
        returns: [
          {
            saleReturnId: 'return-1',
            returnNumber: 'RET-001',
            returnedAt: '2026-05-02T10:00:00Z',
            totalRefundAmount: 100,
            dueReductionAmount: 0,
            payoutAmount: 100,
            isVoided: true,
            voidedAt: '2026-05-03T10:00:00Z',
            voidReason: 'Wrong return',
            items: [],
          },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement?.textContent).toContain('Voided');
    });

    it('renders void reason when present', () => {
      const sale = makeSale({
        returns: [
          {
            saleReturnId: 'return-1',
            returnNumber: 'RET-001',
            returnedAt: '2026-05-02T10:00:00Z',
            totalRefundAmount: 100,
            dueReductionAmount: 0,
            payoutAmount: 100,
            isVoided: true,
            voidedAt: '2026-05-03T10:00:00Z',
            voidReason: 'Duplicate return',
            items: [],
          },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement?.textContent).toContain('Duplicate return');
    });

    it('does not render void reason when not present', () => {
      const sale = makeSale({
        returns: [
          {
            saleReturnId: 'return-1',
            returnNumber: 'RET-001',
            returnedAt: '2026-05-02T10:00:00Z',
            totalRefundAmount: 100,
            dueReductionAmount: 0,
            payoutAmount: 100,
            isVoided: false,
            voidedAt: null,
            voidReason: null,
            items: [],
          },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement?.textContent).not.toContain('Reason');
    });

    it('renders multiple returns separately', () => {
      const sale = makeSale({
        returns: [
          {
            saleReturnId: 'return-1',
            returnNumber: 'RET-001',
            returnedAt: '2026-05-02T10:00:00Z',
            totalRefundAmount: 100,
            dueReductionAmount: 0,
            payoutAmount: 100,
            isVoided: false,
            voidedAt: null,
            voidReason: null,
            items: [],
          },
          {
            saleReturnId: 'return-2',
            returnNumber: 'RET-002',
            returnedAt: '2026-05-03T10:00:00Z',
            totalRefundAmount: 50,
            dueReductionAmount: 0,
            payoutAmount: 50,
            isVoided: false,
            voidedAt: null,
            voidReason: null,
            items: [],
          },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const returnsElement = fixture.nativeElement.querySelector('.invoice__returns');
      expect(returnsElement?.textContent).toContain('RET-001');
      expect(returnsElement?.textContent).toContain('RET-002');
    });
  });

  describe('print styles', () => {
    it('renders A4 invoice structure for print', () => {
      const sale = makeSale();
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const invoiceElement = fixture.nativeElement.querySelector('.invoice');
      expect(invoiceElement).not.toBeNull();
      
      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement).not.toBeNull();
      
      const itemsTable = fixture.nativeElement.querySelector('.invoice__items');
      expect(itemsTable).not.toBeNull();
      
      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement).not.toBeNull();
    });
  });
});
