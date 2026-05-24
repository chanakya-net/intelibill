import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import type { SaleItemDto } from '../../services/sale.models';
import { SaleLineItemsTableComponent } from './sale-line-items-table.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<string, unknown>;

const makeItem = (overrides: Partial<SaleItemDto> = {}): SaleItemDto => ({
  saleItemId: 'line-1',
  itemId: 'item-1',
  itemName: 'Soap',
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
        TranslocoTestingModule.forRoot({ langs: { 'en-IN': enIN }, preloadLangs: true }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SaleLineItemsTableComponent);
    const component = fixture.componentInstance;
    component.items = [makeItem(), makeItem({ itemName: 'Brush', salesPrice: 100, taxRatePercent: 5, savingsAmount: 0 })];
    component.currency = 'INR';
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Soap');
    expect(text).toContain('Brush');
    expect(text).toContain('₹110.00');
    expect(text).toContain('₹20.00');
    expect(text).toContain('-₹20.00');
  });
});
