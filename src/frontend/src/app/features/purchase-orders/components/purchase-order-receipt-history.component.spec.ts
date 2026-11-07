import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Table } from 'primeng/table';
import { describe, expect, it, afterEach } from 'vitest';
import type { PurchaseOrderReceipt } from '../services/purchase-order.service';
import { PurchaseOrderReceiptHistoryComponent } from './purchase-order-receipt-history.component';

describe('PurchaseOrderReceiptHistoryComponent', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('keys both lots by receipt line and keeps original quantity/cost/retail snapshots after current stock changes', () => {
    TestBed.configureTestingModule({
      imports: [
        PurchaseOrderReceiptHistoryComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: {} },
          translocoConfig: { defaultLang: 'en', availableLangs: ['en'] },
        }),
      ],
      providers: [provideRouter([])],
    });
    const base = {
      purchaseOrderLineId: 'po-line',
      itemId: 'item',
      quantity: 4,
      totalPurchaseCost: 20,
      unitCost: 5,
      mrp: 9,
      salesPrice: 8,
      taxRatePercent: 5,
    };
    const receipt: PurchaseOrderReceipt = {
      receiptId: 'receipt',
      receiptNumber: 'POR-1',
      receivedAt: '2026-11-07T10:00:00Z',
      referenceNumber: 'REF',
      notes: null,
      lines: [
        {
          ...base,
          receiptLineId: 'receipt-line-A',
          clientLineId: 'client-A',
          inventoryBatchId: 'batch-A',
          batchNumber: 'A',
          stockTransactionId: 'stock-A',
          ...{ currentQuantity: 1, currentSalesPrice: 999, correctedReceiptCost: 9999 },
        },
        {
          ...base,
          quantity: 6,
          totalPurchaseCost: 30,
          receiptLineId: 'receipt-line-B',
          clientLineId: 'client-B',
          inventoryBatchId: 'batch-B',
          batchNumber: 'B',
          stockTransactionId: 'stock-B',
        },
      ],
    };
    const fixture = TestBed.createComponent(PurchaseOrderReceiptHistoryComponent);
    fixture.componentRef.setInput('receipts', [receipt]);
    fixture.detectChanges();
    const table = fixture.debugElement.query(By.directive(Table)).componentInstance as Table;
    expect(table.dataKey).toBe('receiptLineId');
    expect(receipt.lines.map((line) => line.clientLineId)).toEqual(['client-A', 'client-B']);
    const body = fixture.nativeElement as HTMLElement;
    const rows = [...body.querySelectorAll('tbody tr')];
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('20.00');
    expect(rows[0].textContent).toContain('8.00 / 9.00');
    expect(rows[1].textContent).toContain('30.00');
    expect(rows.map((row) => row.querySelectorAll('td')[1].textContent?.trim())).toEqual([
      '4',
      '6',
    ]);
    expect(body.textContent).not.toContain('999');
    expect(body.textContent).toContain('stock-A');
    expect(body.textContent).toContain('stock-B');
    expect(body.querySelector('a[href*="batchId=batch-A"]')).toBeTruthy();
    expect(body.querySelector('a[href*="batchId=batch-B"]')).toBeTruthy();
  });
});
