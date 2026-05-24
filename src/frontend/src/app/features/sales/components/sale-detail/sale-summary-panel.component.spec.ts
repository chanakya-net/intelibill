import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import type { SaleDto } from '../../services/sale.models';
import { SaleSummaryPanelComponent } from './sale-summary-panel.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<string, unknown>;

const makeSale = (overrides: Partial<SaleDto> = {}): SaleDto => ({
  saleId: 'sale-1',
  invoiceNumber: 'INV-1',
  customerId: 'customer-1',
  customerName: 'Alice',
  customerPhone: '+919999111222',
  paymentMethod: 1,
  soldAt: '2026-05-05T10:00:00Z',
  paidAmount: 220,
  dueAmount: 0,
  totalBeforeDiscount: 220,
  totalDiscountAmount: 0,
  totalAmount: 220,
  totalTaxAmount: 20,
  items: [],
  returns: [],
  warnings: [],
  ...overrides,
});

describe('SaleSummaryPanelComponent', () => {
  it('shows subtotal, tax, total, payment and customer info', async () => {
    await setup();

    const fixture = TestBed.createComponent(SaleSummaryPanelComponent);
    fixture.componentInstance.sale = makeSale({ totalDiscountAmount: 20, totalBeforeDiscount: 240 });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Alice');
    expect(text).toContain('+919999111222');
    expect(text).toContain('Cash');
    expect(text).toContain('Before Discount');
    expect(text).toContain('Tax Amount');
    expect(text).toContain('Grand Total');
    expect(text).toContain('Discount');
    expect(text).toContain('Total Savings');
  });
});

async function setup(): Promise<void> {
  await TestBed.configureTestingModule({
    imports: [
      SaleSummaryPanelComponent,
      TranslocoTestingModule.forRoot({
        langs: { 'en-IN': enIN },
        translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
        preloadLangs: true,
      }),
    ],
  }).compileComponents();
}
