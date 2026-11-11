import { signal } from '@angular/core';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { cancellationFixture } from '../services/sale-cancellation.test-fixture';
import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { SaleInvoiceThermalComponent } from './sale-invoice-thermal.component';
import type {
  InvoiceDocument,
  InvoiceDocumentLine,
  SaleDto,
  SaleItemDto,
} from '../services/sale.models';
import { buildOfflineInvoiceDocument } from '../services/offline-invoice-document.builder';
import { ShopDetails } from '../../shops/services/shop.service';

const enIN = JSON.parse(
  readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8') as string,
) as Record<string, unknown>;

/** Joins a row's cells with single spaces, as a reader sees them. */
function cellText(row: HTMLElement): string {
  return Array.from(row.children)
    .map((cell) => (cell.textContent ?? '').replace(/\s+/g, ' ').trim())
    .join(' ');
}

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
    uom: 'PCS',
    quantityScale: 0,
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
    buyer: {
      name: null,
      phone: null,
      address: null,
      gstin: null,
      stateCode: null,
      stateName: null,
    },
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

  const makeSaleReturn = (
    overrides: Partial<SaleDto['returns'][number]> = {},
  ): SaleDto['returns'][number] => ({
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
      providers: [{ provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } }],
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

  it('marks the sample and suppresses its number, payment status and signatory', () => {
    component.sale = makeSale({ invoiceNumber: 'LIVE-NUMBER' });
    component.shop = makeShop();
    fixture.componentRef.setInput('sampleDocument', makeDocument({ number: 'UNSAFE-NUMBER' }));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('SAMPLE — NOT A VALID INVOICE');
    expect(text).toContain('Basmati Rice 5kg');
    expect(text).not.toContain('UNSAFE-NUMBER');
    expect(text).not.toContain('LIVE-NUMBER');
    expect(text).not.toMatch(/invoice number|payment status|authorised signatory/i);
  });

  it('reformats printed amounts and business days across nine languages without changing facts', () => {
    component.sale = makeSale({
      totalAmount: 123456.78,
      document: makeDocument({ businessDate: '2024-02-29' }),
    });
    component.shop = makeShop();
    const canonical = JSON.stringify(component.sale);
    const language = TestBed.inject(LocalizationService).currentLanguage as ReturnType<
      typeof signal<string>
    >;
    for (const locale of [
      'en-IN',
      'hi-IN',
      'ta-IN',
      'te-IN',
      'bn-IN',
      'ml-IN',
      'kn-IN',
      'mr-IN',
      'gu-IN',
    ]) {
      language.set(locale);
      fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain(
        new Intl.NumberFormat(locale, {
          style: 'currency',
          currency: 'INR',
          minimumFractionDigits: 2,
          maximumFractionDigits: 2,
        }).format(236),
      );
      expect(text).toContain(
        new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(
          new Date('2024-02-29T00:00:00Z'),
        ),
      );
      expect(JSON.stringify(component.sale)).toBe(canonical);
    }
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

  it('keeps the queue state off the printed invoice', () => {
    component.sale = makeSale();
    component.shop = makeShop();
    component.pendingSync = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.thermal-invoice__pending-text')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Sale Queued for Sync');
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
    expect(text).toContain('2 × ₹50.00');
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
    expect(text).toContain('Goods');
    expect(text).toContain('Services');
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
    expect(summary?.textContent).toContain('Partially Paid');
    expect(summary?.textContent).toContain('Subtotal');
  });

  it('renders Unpaid when nothing was paid and Paid when nothing is due', () => {
    component.sale = makeSale({ paidAmount: 0, dueAmount: 95 });
    component.shop = makeShop();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.thermal-invoice__status')?.textContent).toContain(
      'Unpaid',
    );

    fixture = TestBed.createComponent(SaleInvoiceThermalComponent);
    component = fixture.componentInstance;
    component.sale = makeSale({ paidAmount: 95, dueAmount: 0 });
    component.shop = makeShop();
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('.thermal-invoice__status')?.textContent?.trim(),
    ).toBe('Paid');
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
    expect(returnSection.textContent).toContain('11 May 2026');
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

  describe('credit note settlement', () => {
    it('renders credit note settlement line when applied amount > 0', () => {
      const sale = makeSale({ creditNoteAppliedAmount: 100 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Credit Note Settlement');
      expect(fixture.nativeElement.textContent).toMatch(/100/);
    });

    it('renders the codes of the credit notes redeemed on this sale', () => {
      const sale = makeSale({
        creditNoteAppliedAmount: 100,
        creditNoteRedemptions: [{ creditNoteId: 'cn-1', code: 'CN-001', appliedAmount: 100 }],
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Credit Note Settlement (CN-001)');
      expect(fixture.nativeElement.textContent).not.toContain('Discount: -₹100.00');
    });

    it("does not present credit notes issued by this sale's returns as redeemed", () => {
      component.sale = makeSale({
        creditNoteAppliedAmount: 100,
        creditNoteRedemptions: [],
        returns: [
          makeSaleReturn({
            creditNote: {
              creditNoteId: 'cn-9',
              code: 'CN-RETURN-9',
              originalAmount: 40,
              availableBalance: 40,
              expiresAt: null,
              reason: 'Return',
            },
          }),
        ],
      });
      component.shop = makeShop();
      fixture.detectChanges();

      const settlement =
        fixture.nativeElement.querySelector('.thermal-invoice__totals')?.textContent ?? '';
      expect(settlement).toContain('Credit Note Settlement');
      expect(settlement).not.toContain('CN-RETURN-9');
    });

    it('does not render credit note settlement line when applied amount is 0', () => {
      const sale = makeSale({ creditNoteAppliedAmount: 0 });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain('Credit Note Settlement');
    });

    it('settlement line appears after discount and before grand total', () => {
      const sale = makeSale({
        totalBeforeDiscount: 500,
        totalDiscountAmount: 50,
        creditNoteAppliedAmount: 75,
        totalTaxAmount: 81,
        totalAmount: 506,
      });
      const shop = makeShop();

      component.sale = sale;
      component.shop = shop;
      fixture.detectChanges();

      const text = fixture.nativeElement.textContent;
      const discountPos = text.indexOf('Discount');
      const settlementPos = text.indexOf('Credit Note Settlement');
      const grandTotalPos = text.indexOf('Grand Total');

      expect(settlementPos).toBeGreaterThan(discountPos);
      expect(grandTotalPos).toBeGreaterThan(settlementPos);
    });
  });
  describe('legacy sale without a GST document', () => {
    it('renders from the shop with a note that the sale has no GST document', () => {
      component.sale = makeSale({ document: null, isLegacyDocument: true });
      component.shop = makeShop({ name: 'Legacy Shop' });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Legacy Shop');
      expect(
        fixture.nativeElement.querySelector('.thermal-invoice__legacy-note')?.textContent,
      ).toContain('posted without a GST document');
    });

    it('says a queued offline sale without a frozen document is not a GST document copy', () => {
      component.sale = makeSale();
      component.shop = makeShop();
      component.pendingSync = true;
      fixture.detectChanges();

      expect(
        fixture.nativeElement.querySelector('.thermal-invoice__legacy-note')?.textContent,
      ).toContain('is not a copy of a GST document');
    });
  });

  describe('issued GST document', () => {
    function render(
      document: InvoiceDocument,
      sale: Partial<SaleDto> = {},
      shop: Partial<ShopDetails> = {},
    ): string {
      component.sale = makeSale({ document, isLegacyDocument: false, ...sale });
      component.shop = makeShop(shop);
      fixture.detectChanges();
      return (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
    }

    it.each([
      ['TaxInvoice', 'Tax Invoice'],
      ['InvoiceCumBillOfSupply', 'Tax Invoice cum Bill of Supply'],
      ['BillOfSupply', 'Bill of Supply'],
      ['Receipt', 'Receipt'],
    ] as const)('titles a %s document', (documentType, title) => {
      render(makeDocument({ documentType }));

      expect(
        fixture.nativeElement
          .querySelector('.thermal-invoice__document-title')
          ?.textContent?.trim(),
      ).toBe(title);
    });

    it('takes the seller, number and date from the snapshot when the shop was edited after issue', () => {
      const text = render(
        makeDocument(),
        { invoiceNumber: 'INV-OLD-RANDOM' },
        { name: 'Renamed Shop', gstNumber: '29AAGCB7383J1Z4', mobileNumber: '+910000000000' },
      );

      expect(text).toContain('Acme Traders Private Limited');
      expect(text).toContain('Acme Mart');
      expect(text).toContain('GSTIN: 27AAPFU0939F1ZV');
      expect(text).toContain('State: Maharashtra (27)');
      expect(text).toContain('A/2627/000042');
      expect(text).toContain('3 Nov 2026');
      expect(text).not.toContain('Renamed Shop');
      expect(text).not.toContain('29AAGCB7383J1Z4');
      expect(text).not.toContain('+910000000000');
      expect(text).not.toContain('INV-OLD-RANDOM');
    });

    it('prints place of supply, reverse charge and the buyer', () => {
      const text = render(
        makeDocument({
          buyer: {
            name: 'Bharat Stores',
            phone: null,
            address: '5 Market Lane',
            gstin: '29AAGCB7383J1Z4',
            stateCode: '29',
            stateName: 'Karnataka',
          },
        }),
      );

      expect(text).toContain('Place of Supply: Maharashtra (27)');
      expect(text).toContain('Reverse Charge: No');
      expect(text).toContain('Bharat Stores');
      expect(text).toContain('GSTIN: 29AAGCB7383J1Z4');
      expect(text).toContain('Karnataka (29)');
    });

    it('prints lines with HSN/SAC and taxable value from the snapshot', () => {
      const text = render(
        makeDocument(),
        { items: [makeSaleItem({ itemName: 'Live Item Name' })] },
        { gstNumber: null },
      );

      expect(text).toContain('Basmati Rice 5kg');
      expect(text).toContain('HSN/SAC: 10063020');
      expect(text).toContain('Taxable Value: ₹200.00');
      expect(text).not.toContain('Live Item Name');
    });

    it('prints a compact tax summary grouped by component and rate', () => {
      render(
        makeDocument({
          totals: {
            ...makeDocument().totals,
            components: [
              { kind: 'Cgst', ratePercent: 9, taxableBase: 200, amount: 18 },
              { kind: 'Sgst', ratePercent: 9, taxableBase: 200, amount: 18 },
              { kind: 'Cgst', ratePercent: 9, taxableBase: 100, amount: 9 },
              { kind: 'Sgst', ratePercent: 9, taxableBase: 100, amount: 9 },
            ],
            totalTax: 54,
          },
        }),
      );
      const rows = Array.from(
        fixture.nativeElement.querySelectorAll(
          '.thermal-invoice__tax-row',
        ) as NodeListOf<HTMLElement>,
      ).map((row) => cellText(row));

      expect(rows).toEqual(['CGST 9% on ₹300.00 ₹27.00', 'SGST 9% on ₹300.00 ₹27.00']);
    });

    it('prints totals, declarations and the authorised signatory', () => {
      const text = render(
        makeDocument({
          documentType: 'Receipt',
          seller: { ...makeDocument().seller, gstin: null, taxRegime: 'Unregistered' },
          totals: {
            taxableValue: 0,
            exemptValue: 200,
            components: [],
            totalTax: 0,
            roundingAdjustment: 0,
            invoiceTotal: 200,
          },
          declarations: ['NotRegisteredUnderGst'],
        }),
      );

      expect(text).toContain('Total Tax');
      expect(text).toContain('Rounding Adjustment');
      expect(text).toContain('Invoice Total');
      expect(text).toContain('₹200.00');
      expect(text).toContain('not registered under GST');
      expect(text).toContain('For Acme Traders Private Limited');
      expect(text).toContain('Authorised Signatory');
      expect(fixture.nativeElement.querySelector('.thermal-invoice__tax-row')).toBeNull();
      expect(fixture.nativeElement.querySelector('.thermal-invoice__legacy-note')).toBeNull();
    });
  });

  /** A document as the device freezes it offline: built by the same code the finalizer runs. */
  function offlineDocument(): InvoiceDocument {
    const built = buildOfflineInvoiceDocument({
      sellerProfile: {
        seller: {
          legalName: 'Main Shop Pvt Ltd',
          tradeName: 'Main Shop',
          address:
            'Shop No. 14, Ground Floor, Sri Venkateshwara Commercial Complex, 3rd Cross, 80 Feet Road, Koramangala 4th Block, Bengaluru, Karnataka 560034',
          stateCode: '29',
          stateName: 'Karnataka',
          gstin: '29AAPFU0939F1ZR',
          taxRegime: 'Regular',
        },
        eInvoiceStatus: 'BelowThreshold',
        seriesCode: 'A',
      },
      fiscalYearStart: 2026,
      soldAt: '2026-11-03T10:00:00Z',
      customer: null,
      customerName: 'Ravi',
      customerPhone: '9876543210',
      pricing: {
        calculationVersion: 1,
        lines: [
          {
            clientLineId: 'line-1',
            inventoryBatchId: 'batch-1',
            itemId: 'item-1',
            barcode: 'SKU-1',
            itemName: 'Steel Bottle',
            batchNumber: 'B1',
            quantity: 2,
            salesPrice: 50.03,
            mrp: 60,
            costPrice: 30,
            taxRatePercent: 18,
            taxIncluded: false,
            hsnCode: '7323',
            preTaxAmount: 100.06,
            itemDiscountAmount: 0.01,
            saleDiscountAmount: 0,
            taxableAmount: 100.05,
            taxAmount: 18.01,
            lineTotal: 118.06,
            configuredRuleId: null,
            lineType: 'goods',
            serviceId: null,
          },
        ],
        totals: {
          totalBeforeDiscount: 100.06,
          totalDiscount: 0.01,
          totalTax: 18.01,
          grandTotal: 118.06,
          paidAmount: 100,
          dueAmount: 18.06,
        },
      },
    });
    if (!built.ok) throw new Error(built.errorCode);
    return { ...built.draft, number: 'A/2627/000042' };
  }

  it('prints an offline document with its frozen seller, HSN and UOM, tax split and settlement', () => {
    component.sale = makeSale({
      document: offlineDocument(),
      isLegacyDocument: false,
      invoiceNumber: 'A/2627/000042',
      paidAmount: 100,
      dueAmount: 18.06,
      totalAmount: 118.06,
      creditNoteAppliedAmount: 10,
      creditNoteRedemptions: [{ creditNoteId: 'cn-1', code: 'CN-OFFLINE-0001', appliedAmount: 10 }],
    });
    component.shop = makeShop({
      name: 'Renamed Shop',
      gstNumber: '27AAPFU0939F1ZV',
      address: 'Somewhere else',
    });
    component.pendingSync = true;
    fixture.detectChanges();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
    expect(text).toContain('Koramangala 4th Block, Bengaluru, Karnataka 560034');
    expect(text).toContain('29AAPFU0939F1ZR');
    expect(text).not.toContain('27AAPFU0939F1ZV');
    expect(text).not.toContain('Renamed Shop');
    expect(text).toContain('A/2627/000042');
    expect(text).toContain('7323');
    expect(text).toContain('NOS');
    expect(text).toContain('CGST');
    expect(text).toContain('SGST');
    expect(text).toContain('9.01');
    expect(text).toContain('18.06');
    expect(text).toContain('CN-OFFLINE-0001');
    expect(fixture.nativeElement.querySelector('.thermal-invoice__legacy-note')).toBeNull();
  });
  it('prints the cancellation timestamp in the frozen shop timezone across midnight', () => {
    component.sale = makeSale({
      status: 'Cancelled',
      cancellation: {
        ...cancellationFixture,
        cancelledAt: '2026-11-09T20:00:00Z',
        businessDate: '2026-11-10',
        businessTimeZoneId: 'Asia/Kolkata',
      },
    });
    component.shop = makeShop();
    fixture.detectChanges();

    const text = (
      fixture.nativeElement.querySelector('.sale-cancellation-record')?.textContent ?? ''
    ).replace(/\s+/g, ' ');
    expect(text).toContain('2026-11-10 · Nov 10, 2026, 1:30 AM (Asia/Kolkata)');
  });
  it('preserves original amounts and appends frozen cancellation settlement facts', () => {
    component.sale = makeSale({
      status: 'Cancelled',
      cancellation: cancellationFixture,
      paidAmount: 80,
      dueAmount: 120,
      totalAmount: 200,
    });
    component.shop = makeShop({ name: 'Renamed live shop' });
    fixture.detectChanges();
    const fact = fixture.nativeElement.querySelector('.sale-cancellation-record');
    expect(fact?.textContent).toContain('SC-frozen');
    expect(fact?.textContent).not.toContain('3–255');
    expect(fact?.textContent).toContain('Frozen seller');
    expect(fact?.textContent).toContain('Frozen buyer');
    expect(fact?.textContent).toContain('Frozen item');
    expect(fact?.textContent).toContain('120.00');
    expect(fact?.textContent).toContain('80.00');
    expect(fact?.textContent).toContain(
      "Commercial cancellation record — not a GST credit note. Cash refund is recorded from the operator's confirmation.",
    );
    expect(component.sale.paidAmount).toBe(80);
    expect(component.sale.dueAmount).toBe(120);
  });
  it('omits cash-confirmation sentence for a pure-due cancellation', () => {
    component.sale = makeSale({
      status: 'Cancelled',
      cancellation: {
        ...cancellationFixture,
        cashRefundAmount: 0,
        cashRefundConfirmed: false,
        dueReductionAmount: 200,
      },
    });
    component.shop = makeShop();
    fixture.detectChanges();
    const text = fixture.nativeElement.querySelector('.sale-cancellation-record')?.textContent;
    expect(text).toContain('Commercial cancellation record — not a GST credit note.');
    expect(text).not.toContain('Cash refund is recorded');
  });
});
