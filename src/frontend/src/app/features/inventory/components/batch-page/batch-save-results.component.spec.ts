import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { BatchDraftStateService } from '../../services/batch-draft-state.service';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import { BatchSaveResultsComponent } from './batch-save-results.component';

describe('BatchSaveResultsComponent', () => {
  const draftState = {
    pendingRows: signal<any[]>([]),
    loadingDraft: signal(false),
  };

  const suppliersFacade = {
    suppliers: signal<any[]>([]),
    load: vi.fn(),
  };

  function setup() {
    TestBed.configureTestingModule({
      imports: [BatchSaveResultsComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: BatchDraftStateService, useValue: draftState },
        { provide: SuppliersFacade, useValue: suppliersFacade },
      ],
    });

    const fixture = TestBed.createComponent(BatchSaveResultsComponent);
    fixture.componentInstance.saveSummary = {
      requestedCount: 1,
      successCount: 0,
      failedCount: 1,
      succeeded: [],
      failed: [
        {
          clientRowId: 'row-1',
          itemName: 'Milk',
          barcode: 'B001',
          errors: [{ code: 'Inventory.Rule', description: 'Row failed' }],
        },
      ],
    };
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    draftState.pendingRows.set([
      {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode: 'B001',
        itemDescription: null,
        uom: 'ltr',
        batchNumber: 'BN-1',
        quantity: 1,
        totalPurchaseCost: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        purchaseTaxIncluded: true,
        hsnCode: null,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: new Date().toISOString(),
      },
    ]);
    draftState.loadingDraft.set(false);
  });

  it('renders save summary and pending rows', () => {
    const fixture = setup();

    expect(fixture.debugElement.query(By.css('.save-summary'))).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Milk');
    expect(fixture.nativeElement.textContent).toContain('Row failed');
  });

  it('desktop table shows only Product Name, Quantity, Sales Price, Actions columns', () => {
    const fixture = setup();
    const headers = fixture.debugElement.queryAll(By.css('.desktop-pending-table th'));
    const headerTexts = headers.map((h) => h.nativeElement.textContent?.trim());

    expect(headerTexts.length).toBe(4);
    // In test, Transloco shows keys without translating
    expect(headerTexts.some((t) => t?.includes('inventory.name'))).toBe(true);
    expect(headerTexts.some((t) => t?.includes('inventory.quantity'))).toBe(true);
    expect(headerTexts.some((t) => t?.includes('inventory.salesPrice'))).toBe(true);
    expect(headerTexts.some((t) => t?.includes('inventory.actions'))).toBe(true);
  });

  it('desktop table displays error text under Product Name', () => {
    const fixture = setup();
    const productCell = fixture.debugElement.query(By.css('.desktop-pending-table td'));

    expect(productCell).not.toBeNull();
    expect(productCell.nativeElement.textContent).toContain('Milk');
    expect(productCell.nativeElement.textContent).toContain('Row failed');
  });

  it('empty state colspan matches simplified column count', () => {
    draftState.pendingRows.set([]);
    const fixture = setup();

    const emptyTd = fixture.debugElement.query(By.css('.desktop-pending-table td[colspan]'));
    expect(emptyTd?.nativeElement.getAttribute('colspan')).toBe('4');
  });

  it('emits row actions and footer actions', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const edits: string[] = [];
    const removals: string[] = [];
    let clearCount = 0;
    let saveCount = 0;
    component.editRow.subscribe((value) => edits.push(value));
    component.removeRow.subscribe((value) => removals.push(value));
    component.clearAll.subscribe(() => clearCount++);
    component.saveAll.subscribe(() => saveCount++);

    const desktopActionButtons = fixture.debugElement.queryAll(By.css('.desktop-pending-table .row-actions button'));
    expect(desktopActionButtons.length).toBe(2);
    desktopActionButtons[0].triggerEventHandler('click');
    desktopActionButtons[1].triggerEventHandler('click');
    fixture.debugElement.queryAll(By.css('.table-actions button'))[0].triggerEventHandler('click');
    fixture.debugElement.queryAll(By.css('.table-actions button'))[1].triggerEventHandler('click');

    expect(edits).toEqual(['row-1']);
    expect(removals).toEqual(['row-1']);
    expect(clearCount).toBe(1);
    expect(saveCount).toBe(1);
  });

  it('desktop actions buttons respect isSaving state', () => {
    const fixture = setup();
    // Verify initial state allows clicking
    let buttons = fixture.debugElement.queryAll(By.css('.desktop-pending-table .row-actions button'));
    expect(buttons[0].nativeElement.disabled).toBe(false);

    // Now test disabled state by creating new fixture with isSaving true from start
    draftState.pendingRows.set([
      {
        clientRowId: 'row-1',
        itemName: 'Milk',
        barcode: 'B001',
        itemDescription: null,
        uom: 'ltr',
        batchNumber: 'BN-1',
        quantity: 1,
        totalPurchaseCost: 42,
        mrp: 50,
        salesPrice: 48,
        taxRatePercent: 18,
        taxIncluded: true,
        purchaseTaxIncluded: true,
        hsnCode: null,
        expiryDate: null,
        manufacturingDate: null,
        supplierId: null,
        referenceNumber: null,
        notes: null,
        performedAt: new Date().toISOString(),
      },
    ]);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [BatchSaveResultsComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: BatchDraftStateService, useValue: draftState },
        { provide: SuppliersFacade, useValue: suppliersFacade },
      ],
    });
    const fixture2 = TestBed.createComponent(BatchSaveResultsComponent);
    fixture2.componentInstance.isSaving = true;
    fixture2.componentInstance.saveSummary = {
      requestedCount: 1,
      successCount: 0,
      failedCount: 1,
      succeeded: [],
      failed: [
        {
          clientRowId: 'row-1',
          itemName: 'Milk',
          barcode: 'B001',
          errors: [{ code: 'Inventory.Rule', description: 'Row failed' }],
        },
      ],
    };
    fixture2.detectChanges();

    buttons = fixture2.debugElement.queryAll(By.css('.desktop-pending-table .row-actions button'));
    buttons.forEach((btn) => {
      expect(btn.nativeElement.disabled).toBe(true);
    });
  });
});
