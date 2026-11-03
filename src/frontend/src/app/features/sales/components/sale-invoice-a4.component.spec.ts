import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi } from 'vitest';

import { SaleInvoiceA4Component } from './sale-invoice-a4.component';
import type { InvoiceDocument, InvoiceDocumentLine, SaleDto, SaleItemDto } from '../services/sale.models';
import { ShopDetails } from '../../shops/services/shop.service';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8') as string) as Record<string, unknown>;

/** Joins a row's cells with single spaces, as a reader sees them. */
function cellText(row: HTMLElement): string {
  return Array.from(row.children)
    .map((cell) => (cell.textContent ?? '').replace(/\s+/g, ' ').trim())
    .join(' ');
}

describe('SaleInvoiceA4Component', () => {
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
    creditNoteAppliedAmount: 0,
    creditNoteRedemptions: [],
    items: [makeSaleItem()],
    returns: [],
    warnings: [],
    document: null,
    isLegacyDocument: true,
    ...overrides,
  });

  const makeLine = (overrides: Partial<InvoiceDocumentLine> = {}): InvoiceDocumentLine => ({
    lineNumber: 1,
    description: 'Basmati Rice 5kg',
    hsnOrSac: '10063020',
    uom: 'NOS',
    quantity: 2,
    unitPrice: 100,
    discountAmount: 0,
    taxableValue: 200,
    taxRatePercent: 18,
    isExempt: false,
    taxComponents: [
      { kind: 'Cgst', ratePercent: 9, taxableBase: 200, amount: 18 },
      { kind: 'Sgst', ratePercent: 9, taxableBase: 200, amount: 18 },
    ],
    lineTotal: 236,
    ...overrides,
  });

  const makeDocument = (overrides: Partial<InvoiceDocument> = {}): InvoiceDocument => ({
    version: 2,
    documentType: 'TaxInvoice',
    issuedAt: '2026-11-03T05:30:00Z',
    businessDate: '2026-11-03',
    fiscalYearStart: 2026,
    seriesCode: 'A',
    number: 'A/2627/000042',
    seller: {
      legalName: 'Acme Traders Private Limited',
      tradeName: 'Acme Mart',
      address: '12 Station Road, Pune, Maharashtra, 411001',
      stateCode: '27',
      stateName: 'Maharashtra',
      gstin: '27AAPFU0939F1ZV',
      taxRegime: 'Regular',
    },
    buyer: { name: null, phone: null, address: null, gstin: null, stateCode: null, stateName: null },
    placeOfSupplyStateCode: '27',
    placeOfSupplyStateName: 'Maharashtra',
    isInterstate: false,
    reverseCharge: false,
    lines: [makeLine()],
    totals: {
      taxableValue: 200,
      exemptValue: 0,
      components: [
        { kind: 'Cgst', ratePercent: 9, taxableBase: 200, amount: 18 },
        { kind: 'Sgst', ratePercent: 9, taxableBase: 200, amount: 18 },
      ],
      totalTax: 36,
      roundingAdjustment: 0,
      invoiceTotal: 236,
    },
    declarations: [],
    signatureMode: 'AuthorisedSignatory',
    taxCalculation: 'component-split/1',
    ...overrides,
  });

  const makeSaleReturn = (overrides: Partial<SaleDto['returns'][number]> = {}): SaleDto['returns'][number] => ({
    saleReturnId: 'return-1',
    returnNumber: 'RET-001',
    returnedAt: '2026-05-10T10:00:00Z',
    totalRefundAmount: 0,
    dueReductionAmount: 0,
    payoutAmount: 0,
    isVoided: false,
    voidedAt: null,
    voidReason: null,
    items: [],
    creditNote: null,
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

    it('shows pending sync marker when enabled', () => {
      const sale = makeSale();
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      component.pendingSync = true;
      fixture.detectChanges();

      const marker = fixture.nativeElement.querySelector('.invoice__pending-sync');
      expect(marker).not.toBeNull();
      expect(marker?.textContent).toContain('Sale Queued for Sync');
    });

    it('does not show pending sync marker when disabled', () => {
      const sale = makeSale();
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const marker = fixture.nativeElement.querySelector('.invoice__pending-sync');
      expect(marker).toBeNull();
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

    it('labels the taxable column as the taxable value, not the tax amount', () => {
      component.sale = makeSale();
      component.shop = makeShop();
      fixture.detectChanges();

      const taxableHeader = fixture.nativeElement.querySelector('th.invoice__col-taxable')?.textContent ?? '';
      expect(taxableHeader.trim()).toBe('Taxable Value');
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

    it('renders payment status as Unpaid when nothing was paid', () => {
      component.sale = makeSale({ paidAmount: 0, dueAmount: 100, paymentMethod: 4 });
      component.shop = makeShop();
      fixture.detectChanges();

      const status = fixture.nativeElement.querySelector('.invoice__payment-status')?.textContent ?? '';
      expect(status).toContain('Unpaid');
      expect(status).not.toContain('Partially');
    });

    it('renders payment status as Partially Paid only when some amount was paid', () => {
      component.sale = makeSale({ paidAmount: 40, dueAmount: 60 });
      component.shop = makeShop();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.invoice__payment-status')?.textContent).toContain('Partially Paid');
    });

    it('renders payment status as Paid when nothing is due', () => {
      component.sale = makeSale({ paidAmount: 100, dueAmount: 0 });
      component.shop = makeShop();
      fixture.detectChanges();

      const status = fixture.nativeElement.querySelector('.invoice__payment-status')?.textContent ?? '';
      expect(status.trim()).toBe('Paid');
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

  describe('GST/HSN conditional rendering', () => {
    it('GST shop: shows GSTIN in header', () => {
      component.sale = makeSale();
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).toContain('GSTIN12345678');
    });

    it('non-GST shop: hides GSTIN in header', () => {
      component.sale = makeSale();
      component.shop = makeShop({ gstNumber: null });
      fixture.detectChanges();

      const headerElement = fixture.nativeElement.querySelector('.invoice__header');
      expect(headerElement?.textContent).not.toContain('GSTIN');
    });

    it('GST shop: shows HSN column header in item table', () => {
      component.sale = makeSale();
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('HSN');
    });

    it('non-GST shop: hides HSN column header in item table', () => {
      component.sale = makeSale();
      component.shop = makeShop({ gstNumber: null });
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).not.toContain('HSN');
    });

    it('GST shop: shows HSN value in item row', () => {
      component.sale = makeSale({ items: [makeSaleItem({ hsnCode: '1234' })] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('1234');
    });

    it('GST shop: blank HSN renders blank, not N/A', () => {
      component.sale = makeSale({ items: [makeSaleItem({ hsnCode: null })] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).not.toContain('N/A');
    });

    it('non-GST shop: hides HSN cells in item rows', () => {
      component.sale = makeSale({ items: [makeSaleItem({ hsnCode: '5678' })] });
      component.shop = makeShop({ gstNumber: null });
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).not.toContain('5678');
    });

    it('non-GST shop: tax column still visible', () => {
      component.sale = makeSale({ items: [makeSaleItem({ taxRatePercent: 12 })] });
      component.shop = makeShop({ gstNumber: null });
      fixture.detectChanges();

      const tableElement = fixture.nativeElement.querySelector('table');
      expect(tableElement?.textContent).toContain('12%');
    });

    it('mixed bill: groups goods and services and labels HSN/SAC', () => {
      const goodsItem = makeSaleItem({ lineType: 'Goods', itemName: 'Goods Item', hsnCode: 'HSN123' });
      const serviceItem = makeSaleItem({ lineType: 'Service', itemName: 'Service Item', hsnCode: 'SAC456' });
      component.sale = makeSale({ items: [goodsItem, serviceItem] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const tableText = fixture.nativeElement.textContent;
      expect(tableText).toMatch(/Goods|sales\.newSale\.cart\.goodsSection/);
      expect(tableText).toMatch(/Services|sales\.newSale\.cart\.servicesSection/);
      expect(tableText).toContain('Goods Item');
      expect(tableText).toContain('Service Item');
      expect(tableText).toMatch(/HSN\/SAC|services\.hsnSac/);
    });

    it('goods-only bill: remains flat and does not show section headers', () => {
      const goodsItem1 = makeSaleItem({ lineType: 'Goods', itemName: 'Goods Item 1' });
      const goodsItem2 = makeSaleItem({ lineType: 'Goods', itemName: 'Goods Item 2' });
      component.sale = makeSale({ items: [goodsItem1, goodsItem2] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const tableText = fixture.nativeElement.textContent;
      const sectionHeaders = fixture.nativeElement.querySelectorAll('.invoice__section-header-row');
      expect(sectionHeaders.length).toBe(0);
      expect(tableText).not.toContain('sales.newSale.cart.servicesSection');
      expect(tableText).toContain('Goods Item 1');
      expect(tableText).toContain('Goods Item 2');
    });

    it('goods-only GST bill: header stays plain HSN, not HSN/SAC', () => {
      component.sale = makeSale({ items: [makeSaleItem({ lineType: 'Goods', hsnCode: 'HSN123' })] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const headerText = fixture.nativeElement.querySelector('.invoice__items-header')?.textContent ?? '';
      expect(headerText).not.toContain('HSN/SAC');
      expect(headerText).toContain('HSN');
    });

    it('service-only GST bill: header shows HSN/SAC and stays flat', () => {
      component.sale = makeSale({ items: [makeSaleItem({ lineType: 'Service', itemName: 'Service Item', hsnCode: 'SAC456' })] });
      component.shop = makeShop({ gstNumber: 'GSTIN12345678' });
      fixture.detectChanges();

      const headerText = fixture.nativeElement.querySelector('.invoice__items-header')?.textContent ?? '';
      const sectionHeaders = fixture.nativeElement.querySelectorAll('.invoice__section-header-row');
      expect(headerText).toContain('HSN/SAC');
      expect(sectionHeaders.length).toBe(0);
    });
  });

  describe('credit note settlement', () => {
    it('renders credit note settlement line when applied amount > 0', () => {
      const sale = makeSale({ creditNoteAppliedAmount: 150 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toContain('Credit Note Settlement');
      expect(totalsElement?.textContent).toMatch(/150/);
    });

    it('renders the codes of the credit notes redeemed on this sale', () => {
      const sale = makeSale({
        creditNoteAppliedAmount: 150,
        creditNoteRedemptions: [
          { creditNoteId: 'cn-1', code: 'CN-001', appliedAmount: 100 },
          { creditNoteId: 'cn-2', code: 'CN-002', appliedAmount: 50 },
        ],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toContain('Credit Note Settlement (CN-001, CN-002)');
      expect(totalsElement?.textContent).not.toContain('Discount: -₹150.00');
    });

    it('does not present credit notes issued by this sale\'s returns as redeemed', () => {
      const sale = makeSale({
        creditNoteAppliedAmount: 150,
        creditNoteRedemptions: [{ creditNoteId: 'cn-1', code: 'CN-001', appliedAmount: 150 }],
        returns: [makeSaleReturn({ creditNote: { creditNoteId: 'cn-9', code: 'CN-RETURN-9', originalAmount: 40, availableBalance: 40, expiresAt: null, reason: 'Return' } })],
      });

      component.sale = sale;
      component.shop = makeShop();
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).toContain('Credit Note Settlement (CN-001)');
      expect(totalsElement?.textContent).not.toContain('CN-RETURN-9');
    });

    it('does not render credit note settlement line when applied amount is 0', () => {
      const sale = makeSale({ creditNoteAppliedAmount: 0 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsElement = fixture.nativeElement.querySelector('.invoice__totals');
      expect(totalsElement?.textContent).not.toContain('Credit Note Settlement');
    });

    it('renders settlement line after discount and before tax', () => {
      const sale = makeSale({
        totalBeforeDiscount: 1000,
        totalDiscountAmount: 100,
        creditNoteAppliedAmount: 50,
        totalTaxAmount: 100,
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const totalsRows = fixture.nativeElement.querySelectorAll('.invoice__totals-row');
      const texts = Array.from(totalsRows).map((row: unknown) => (row as Element).textContent);
      const discountIdx = texts.findIndex(t => typeof t === 'string' && t.includes('Discount'));
      const settlementIdx = texts.findIndex(t => typeof t === 'string' && t.includes('Credit Note Settlement'));
      const taxIdx = texts.findIndex(t => typeof t === 'string' && t.includes('Tax'));

      expect(settlementIdx).toBeGreaterThan(discountIdx);
      expect(taxIdx).toBeGreaterThan(settlementIdx);
    });
  });
  describe('legacy sale without a GST document', () => {
    it('renders from the shop and notes that the sale has no GST document', () => {
      component.sale = makeSale({ document: null, isLegacyDocument: true });
      component.shop = makeShop({ name: 'Legacy Shop' });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.invoice__header')?.textContent).toContain('Legacy Shop');
      expect(fixture.nativeElement.querySelector('.invoice__legacy-note')?.textContent).toContain(
        'posted without a GST document',
      );
      expect(fixture.nativeElement.querySelector('.invoice__tax-summary')).toBeNull();
    });

    it('does not add the legacy note to a queued offline sale', () => {
      component.sale = makeSale({ document: null, isLegacyDocument: true });
      component.shop = makeShop();
      component.pendingSync = true;
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.invoice__legacy-note')).toBeNull();
    });

  });

  describe('issued GST document', () => {
    function render(document: InvoiceDocument, sale: Partial<SaleDto> = {}, shop: Partial<ShopDetails> = {}): string {
      component.sale = makeSale({ document, isLegacyDocument: false, ...sale });
      component.shop = makeShop(shop);
      fixture.detectChanges();
      return fixture.nativeElement.textContent ?? '';
    }

    it.each([
      ['TaxInvoice', 'Tax Invoice'],
      ['InvoiceCumBillOfSupply', 'Tax Invoice cum Bill of Supply'],
      ['BillOfSupply', 'Bill of Supply'],
      ['Receipt', 'Receipt'],
    ] as const)('titles a %s document', (documentType, title) => {
      render(makeDocument({ documentType }));

      expect(fixture.nativeElement.querySelector('.invoice__document-title')?.textContent?.trim()).toBe(title);
    });

    it('prints the document number and business date, not the sale record', () => {
      const text = render(makeDocument(), { invoiceNumber: 'INV-20261103-abcdef12', soldAt: '2026-11-02T20:00:00Z' });

      expect(text).toContain('A/2627/000042');
      expect(text).toContain('03/11/2026');
      expect(text).not.toContain('INV-20261103-abcdef12');
    });

    it('takes the seller from the snapshot even when the shop was edited after issue', () => {
      const text = render(makeDocument(), {}, { name: 'Renamed Shop', gstNumber: '29AAGCB7383J1Z4', address: 'New Address', mobileNumber: '+910000000000' });
      const header = fixture.nativeElement.querySelector('.invoice__header')?.textContent ?? '';

      expect(header).toContain('Acme Traders Private Limited');
      expect(header).toContain('Acme Mart');
      expect(header).toContain('12 Station Road, Pune, Maharashtra, 411001');
      expect(header).toContain('GSTIN: 27AAPFU0939F1ZV');
      expect(header).toContain('State: Maharashtra (27)');
      expect(text).not.toContain('Renamed Shop');
      expect(text).not.toContain('29AAGCB7383J1Z4');
      expect(text).not.toContain('New Address');
      expect(text).not.toContain('+910000000000');
    });

    it('does not repeat the trade name when it is the legal name', () => {
      render(makeDocument({ seller: { ...makeDocument().seller, tradeName: 'Acme Traders Private Limited' } }));

      expect(fixture.nativeElement.querySelector('.invoice__shop-trade-name')).toBeNull();
    });

    it('omits the GSTIN line for an unregistered seller', () => {
      render(
        makeDocument({
          documentType: 'Receipt',
          seller: { ...makeDocument().seller, gstin: null, taxRegime: 'Unregistered' },
          declarations: ['NotRegisteredUnderGst'],
        }),
        {},
        { gstNumber: '27AAPFU0939F1ZV' },
      );

      expect(fixture.nativeElement.querySelector('.invoice__header')?.textContent).not.toContain('GSTIN');
      expect(fixture.nativeElement.querySelector('.invoice__declarations')?.textContent).toContain('not registered under GST');
    });

    it('prints place of supply and reverse charge', () => {
      const text = render(makeDocument({ placeOfSupplyStateCode: '29', placeOfSupplyStateName: 'Karnataka', isInterstate: true }));

      expect(text).toContain('Place of Supply');
      expect(text).toContain('Karnataka (29)');
      expect(text).toContain('Reverse Charge: No');
    });

    it('prints the buyer block from the snapshot', () => {
      render(
        makeDocument({
          buyer: {
            name: 'Bharat Stores',
            phone: '+919811111111',
            address: '5 Market Lane, Bengaluru',
            gstin: '29AAGCB7383J1Z4',
            stateCode: '29',
            stateName: 'Karnataka',
          },
        }),
        { customerName: 'Live Customer Name' },
      );
      const buyer = fixture.nativeElement.querySelector('.invoice__customer')?.textContent ?? '';

      expect(buyer).toContain('Bill To');
      expect(buyer).toContain('Bharat Stores');
      expect(buyer).toContain('5 Market Lane, Bengaluru');
      expect(buyer).toContain('GSTIN: 29AAGCB7383J1Z4');
      expect(buyer).toContain('Karnataka (29)');
      expect(buyer).not.toContain('Live Customer Name');
    });

    it('prints each line with its HSN/SAC and taxable value from the snapshot', () => {
      render(
        makeDocument({
          lines: [makeLine({ description: 'Hair Cut', hsnOrSac: '999721', uom: 'NA', taxableValue: 150, lineTotal: 177 })],
        }),
        { items: [makeSaleItem({ itemName: 'Live Item Name', hsnCode: '1111' })] },
        { gstNumber: null },
      );
      const table = fixture.nativeElement.querySelector('.invoice__items')?.textContent ?? '';

      expect(table).toContain('HSN/SAC');
      expect(table).toContain('Hair Cut');
      expect(table).toContain('999721');
      expect(table).toContain('₹150.00');
      expect(table).toContain('₹177.00');
      expect(table).not.toContain('Live Item Name');
    });

    it('summarises intrastate tax by component and rate', () => {
      render(
        makeDocument({
          lines: [makeLine(), makeLine({ lineNumber: 2 }), makeLine({ lineNumber: 3, taxRatePercent: 5 })],
          totals: {
            taxableValue: 500,
            exemptValue: 0,
            components: [
              { kind: 'Cgst', ratePercent: 9, taxableBase: 200, amount: 18 },
              { kind: 'Sgst', ratePercent: 9, taxableBase: 200, amount: 18 },
              { kind: 'Cgst', ratePercent: 9, taxableBase: 200, amount: 18 },
              { kind: 'Sgst', ratePercent: 9, taxableBase: 200, amount: 18 },
              { kind: 'Cgst', ratePercent: 2.5, taxableBase: 100, amount: 2.5 },
              { kind: 'Sgst', ratePercent: 2.5, taxableBase: 100, amount: 2.5 },
            ],
            totalTax: 77,
            roundingAdjustment: 0,
            invoiceTotal: 577,
          },
        }),
      );
      const rows = Array.from(
        fixture.nativeElement.querySelectorAll('.invoice__tax-summary tbody tr') as NodeListOf<HTMLElement>,
      ).map((row) => cellText(row));

      expect(rows).toEqual([
        'CGST 9% ₹400.00 ₹36.00',
        'CGST 2.5% ₹100.00 ₹2.50',
        'SGST 9% ₹400.00 ₹36.00',
        'SGST 2.5% ₹100.00 ₹2.50',
      ]);
    });

    it('summarises interstate tax as IGST and union territory tax as UTGST', () => {
      render(
        makeDocument({
          isInterstate: true,
          totals: {
            ...makeDocument().totals,
            components: [
              { kind: 'Igst', ratePercent: 18, taxableBase: 200, amount: 36 },
              { kind: 'Utgst', ratePercent: 9, taxableBase: 100, amount: 9 },
            ],
          },
        }),
      );
      const summary = fixture.nativeElement.querySelector('.invoice__tax-summary')?.textContent ?? '';

      expect(summary).toContain('IGST');
      expect(summary).toContain('18%');
      expect(summary).toContain('UTGST');
      expect(summary).not.toContain('CGST');
    });

    it('omits the tax summary when the document carries no tax', () => {
      render(
        makeDocument({
          documentType: 'BillOfSupply',
          lines: [makeLine({ taxRatePercent: 0, isExempt: true, taxComponents: [], lineTotal: 200 })],
          totals: { taxableValue: 0, exemptValue: 200, components: [], totalTax: 0, roundingAdjustment: 0, invoiceTotal: 200 },
        }),
      );

      expect(fixture.nativeElement.querySelector('.invoice__tax-summary')).toBeNull();
      expect(fixture.nativeElement.querySelector('.invoice__totals')?.textContent).toContain('Exempt Value');
    });

    it('prints total tax, rounding adjustment and invoice total from the snapshot', () => {
      render(
        makeDocument({ totals: { ...makeDocument().totals, totalTax: 36, roundingAdjustment: 0.4, invoiceTotal: 236.4 } }),
        { totalAmount: 999, totalTaxAmount: 888 },
      );
      const totals = fixture.nativeElement.querySelector('.invoice__totals')?.textContent ?? '';

      expect(totals).toContain('Total Tax');
      expect(totals).toContain('₹36.00');
      expect(totals).toContain('Rounding Adjustment');
      expect(totals).toContain('₹0.40');
      expect(totals).toContain('Invoice Total');
      expect(totals).toContain('₹236.40');
      expect(totals).not.toContain('₹999.00');
      expect(totals).not.toContain('₹888.00');
    });

    it('translates declarations and prints the authorised signatory line', () => {
      render(
        makeDocument({
          documentType: 'BillOfSupply',
          seller: { ...makeDocument().seller, taxRegime: 'Composition' },
          declarations: ['CompositionTaxablePerson', 'EInvoiceExemption'],
        }),
      );
      const declarations = fixture.nativeElement.querySelector('.invoice__declarations')?.textContent ?? '';
      const signatory = fixture.nativeElement.querySelector('.invoice__signatory')?.textContent ?? '';

      expect(declarations).toContain('Composition taxable person, not eligible to collect tax on supplies');
      expect(declarations).toContain('rule 48');
      expect(declarations).not.toContain('CompositionTaxablePerson');
      expect(signatory).toContain('For Acme Traders Private Limited');
      expect(signatory).toContain('Authorised Signatory');
    });

    it('shows no legacy note on an issued document', () => {
      render(makeDocument());

      expect(fixture.nativeElement.querySelector('.invoice__legacy-note')).toBeNull();
    });

    it('counts a redeemed credit note toward a part payment on an issued document', () => {
      render(makeDocument(), {
        paidAmount: 0,
        dueAmount: 136,
        creditNoteAppliedAmount: 100,
        creditNoteRedemptions: [{ creditNoteId: 'cn-1', code: 'CN-777', appliedAmount: 100 }],
      });
      const totals = fixture.nativeElement.querySelector('.invoice__totals')?.textContent ?? '';

      expect(totals).toContain('Credit Note Settlement (CN-777)');
      expect(fixture.nativeElement.querySelector('.invoice__payment-status')?.textContent).toContain('Partially Paid');
    });
  });
});
