import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import type { SaleItemDto } from '../../services/sale.models';
import { SaleLineItemsTableComponent } from './sale-line-items-table.component';

const enIN = JSON.parse(
  readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8'),
) as Record<string, unknown>;

const makeItem = (overrides: Partial<SaleItemDto> = {}): SaleItemDto => ({
  saleItemId: 'line-1',
  lineType: 'Goods',
  itemId: 'item-1',
  serviceId: null,
  itemName: 'Soap',
  lineCode: 'ITEM-001',
  inventoryBatchId: 'batch-1',
  quantity: 2,
  salesPrice: 110,
  originalSalesPrice: 120,
  finalSalesPrice: 110,
  preTaxAmountBeforeDiscount: 200,
  itemDiscountAmount: 10,
  saleDiscountAmount: 0,
  taxableAmount: 200,
  taxAmount: 20,
  totalAmount: 220,
  savingsAmount: 20,
  taxRatePercent: 10,
  isPriceIncludingTax: false,
  hasPriceMismatch: false,
  returnedQuantity: 0,
  returnableQuantity: 2,
  returnStatus: 'Returnable',
  hsnCode: null,
  ...overrides,
});

describe('SaleLineItemsTableComponent', () => {
  it('renders line item subtotals tax total, and discount', async () => {
    await TestBed.configureTestingModule({
      imports: [
        SaleLineItemsTableComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleLineItemsTableComponent);
    const component = fixture.componentInstance;
    component.items = [
      makeItem(),
      makeItem({ itemName: 'Brush', salesPrice: 100, taxRatePercent: 5, savingsAmount: 0 }),
    ];
    component.currency = 'INR';
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Soap');
    expect(text).toContain('Brush');
    expect(text).toContain('₹110.00');
    expect(text).toContain('₹22.00');
    expect(text).toContain('-₹20.00');
  });

  it('renders authoritative discounted totals for tax-exclusive lines and sections', async () => {
    await TestBed.configureTestingModule({
      imports: [
        SaleLineItemsTableComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleLineItemsTableComponent);
    fixture.componentInstance.items = [
      makeItem({
        quantity: 2,
        salesPrice: 110,
        totalAmount: 205,
        itemDiscountAmount: 15,
      }),
      makeItem({
        saleItemId: 'line-2',
        itemName: 'Brush',
        quantity: 1,
        salesPrice: 100,
        totalAmount: 95,
        itemDiscountAmount: 5,
        savingsAmount: 5,
      }),
    ];
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('₹205.00');
    expect(text).toContain('₹95.00');
    expect(text).toContain('₹300.00');
    expect(text).not.toContain('₹320.00');
  });

  it('groups goods and services only for mixed bills', async () => {
    await TestBed.configureTestingModule({
      imports: [
        SaleLineItemsTableComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleLineItemsTableComponent);
    const component = fixture.componentInstance;
    component.items = [
      makeItem({ itemName: 'Soap', lineType: 'Goods', hsnCode: 'HSN1' }),
      makeItem({
        saleItemId: 'line-2',
        itemName: 'Repair',
        lineType: 'Service',
        itemId: null,
        serviceId: 'svc-1',
        inventoryBatchId: null,
        lineCode: 'SAC1',
        hsnCode: 'SAC1',
      }),
    ];
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toMatch(/Goods|sales\.newSale\.cart\.goodsSection/);
    expect(text).toMatch(/Services|sales\.newSale\.cart\.servicesSection/);
    expect(text).toMatch(/HSN\/SAC|services\.hsnSac/);
  });

  it('goods-only bill: uses the receipt section layout with HSN/SAC details', async () => {
    await TestBed.configureTestingModule({
      imports: [
        SaleLineItemsTableComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleLineItemsTableComponent);
    const component = fixture.componentInstance;
    component.items = [
      makeItem({ itemName: 'Soap', lineType: 'Goods', hsnCode: 'HSN1' }),
      makeItem({ saleItemId: 'line-2', itemName: 'Brush', lineType: 'Goods' }),
    ];
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Goods sold');
    expect(text).toContain('2 items');
    expect(text).toMatch(/HSN\/SAC|services\.hsnSac/);
    expect(text).toContain('HSN1');
  });

  it('service-only bill: uses the receipt section layout with HSN/SAC details', async () => {
    await TestBed.configureTestingModule({
      imports: [
        SaleLineItemsTableComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleLineItemsTableComponent);
    const component = fixture.componentInstance;
    component.items = [
      makeItem({
        itemName: 'Repair',
        lineType: 'Service',
        itemId: null,
        serviceId: 'svc-1',
        inventoryBatchId: null,
        lineCode: 'SAC1',
        hsnCode: 'SAC1',
      }),
    ];
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Services sold');
    expect(text).toContain('1 service');
    expect(text).toMatch(/HSN\/SAC|services\.hsnSac/);
  });
});
