import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { API_BASE_URL } from '../../../../core/auth/auth.constants';
import {
  InventoryAdjustmentHistoryItem,
  InventoryBatchDto,
} from '../../services/inventory.service';
import { InventoryAdjustmentsPageComponent } from './inventory-adjustments-page.component';

describe('InventoryAdjustmentsPageComponent', () => {
  let component: InventoryAdjustmentsPageComponent;
  let fixture: ComponentFixture<InventoryAdjustmentsPageComponent>;
  let httpMock: HttpTestingController;

  const adjustment: InventoryAdjustmentHistoryItem = {
    adjustmentId: 'adjustment-1',
    adjustmentNumber: 'ADJ-0001',
    itemId: 'item-1',
    itemName: 'Rice',
    barcode: '111',
    batchId: 'batch-1',
    batchNumber: 'BATCH-001',
    direction: 'Decrease',
    reason: 'Damaged',
    quantity: 2,
    unitCost: 100,
    costImpact: -200,
    batchQuantityBefore: 10,
    batchQuantityAfter: 8,
    inventoryQuantityBefore: 20,
    inventoryQuantityAfter: 18,
    performedAt: '2026-05-05T08:30:00.000Z',
    performedByUserId: 'user-1',
    performedByDisplayName: 'Test User',
    notes: 'Damaged',
    isVoided: false,
    voidedAt: null,
    voidedByUserId: null,
    voidedByDisplayName: null,
    voidReason: null,
    reversalStockTransactionId: null,
  };

  const batches: InventoryBatchDto[] = [
    {
      id: 'batch-1',
      shopId: 'shop-1',
      itemId: 'item-1',
      itemName: 'Rice',
      barcode: '111',
      batchNumber: 'BATCH-001',
      quantity: 10,
      originalQuantity: 10,
      costPrice: 100,
      mrp: 150,
      salesPrice: 140,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      supplierName: null,
      isVoided: false,
      createdAt: '2026-05-01T00:00:00.000Z',
      updatedAt: null,
    },
    {
      id: 'batch-empty',
      shopId: 'shop-1',
      itemId: 'item-2',
      itemName: 'Sugar',
      barcode: '222',
      batchNumber: 'BATCH-EMPTY',
      quantity: 0,
      originalQuantity: 5,
      costPrice: 50,
      mrp: 70,
      salesPrice: 65,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      supplierName: null,
      isVoided: false,
      createdAt: '2026-05-01T00:00:00.000Z',
      updatedAt: null,
    },
    {
      id: 'batch-voided',
      shopId: 'shop-1',
      itemId: 'item-3',
      itemName: 'Voided Oil',
      barcode: '333',
      batchNumber: 'BATCH-VOID',
      quantity: 3,
      originalQuantity: 3,
      costPrice: 200,
      mrp: 250,
      salesPrice: 240,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      supplierName: null,
      isVoided: true,
      createdAt: '2026-05-01T00:00:00.000Z',
      updatedAt: null,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        InventoryAdjustmentsPageComponent,
        HttpClientTestingModule,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(InventoryAdjustmentsPageComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  function flushInitialLoad(): void {
    const historyReq = httpMock.expectOne(
      (req) => req.url === `${API_BASE_URL}/inventory/adjustments`,
    );
    expect(historyReq.request.params.get('pageNumber')).toBe('1');
    expect(historyReq.request.params.get('pageSize')).toBe('20');
    historyReq.flush({ items: [adjustment], totalCount: 1, pageNumber: 1, pageSize: 20 });

    const batchesReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches`);
    batchesReq.flush(batches);
  }

  it('loads adjustment history and non-voided batches on init', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.adjustments()).toHaveLength(1);
    expect(component.adjustments()[0].adjustmentNumber).toBe('ADJ-0001');
    expect(component.availableBatches().map((batch) => batch.id)).toEqual([
      'batch-1',
      'batch-empty',
    ]);
  });

  it('renders desktop table and mobile cards for loaded history', () => {
    fixture.detectChanges();
    flushInitialLoad();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.desktop-table')?.textContent).toContain('ADJ-0001');
    expect(host.querySelector('.mobile-grid-container')?.textContent).toContain('Rice');
  });

  it('applies filters with server-side paging', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.filterForm.patchValue({
      itemId: 'item-1',
      batchId: 'batch-1',
      direction: 'Decrease',
      reason: 'Damaged',
      from: '2026-05-01',
      to: '2026-05-05',
      includeVoided: true,
    });
    component.onApplyFilters();

    const req = httpMock.expectOne((request) => request.url === `${API_BASE_URL}/inventory/adjustments`);
    expect(req.request.params.get('pageNumber')).toBe('1');
    expect(req.request.params.get('itemId')).toBe('item-1');
    expect(req.request.params.get('batchId')).toBe('batch-1');
    expect(req.request.params.get('direction')).toBe('Decrease');
    expect(req.request.params.get('reason')).toBe('Damaged');
    expect(req.request.params.get('from')).toBe('2026-05-01');
    expect(req.request.params.get('to')).toBe('2026-05-05');
    expect(req.request.params.get('includeVoided')).toBe('true');
    req.flush({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20 });
  });

  it('loads the requested page from the server', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.onPageChange(3);

    const req = httpMock.expectOne((request) => request.url === `${API_BASE_URL}/inventory/adjustments`);
    expect(req.request.params.get('pageNumber')).toBe('3');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({ items: [], totalCount: 41, pageNumber: 3, pageSize: 20 });
  });

  it('searches batches locally and blocks decrease from zero-quantity batches', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.openNewAdjustment();
    component.onBatchSearch({ query: 'sug' });
    expect(component.batchSuggestions().map((batch) => batch.id)).toEqual(['batch-empty']);

    component.onSelectBatch(batches[1]);
    expect(component.selectedBatchDecreaseBlocked()).toBe(true);
    expect(component.adjustmentForm.invalid).toBe(true);

    component.adjustmentForm.controls.direction.setValue('Increase');
    expect(component.selectedBatchDecreaseBlocked()).toBe(false);
    expect(component.adjustmentForm.valid).toBe(true);
  });

  it('creates an adjustment and refreshes history', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.openNewAdjustment();
    component.onSelectBatch(batches[0]);
    component.adjustmentForm.patchValue({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 2,
      performedAt: '2026-05-05T08:30',
      notes: 'Damaged while unloading',
    });

    component.onSaveAdjustment();

    const adjustReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches/batch-1/adjust`);
    expect(adjustReq.request.method).toBe('POST');
    expect(adjustReq.request.body).toEqual({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 2,
      performedAt: new Date('2026-05-05T08:30').toISOString(),
      notes: 'Damaged while unloading',
    });
    adjustReq.flush({
      adjustmentId: 'adjustment-2',
      adjustmentNumber: 'ADJ-0002',
      quantity: 2,
      unitCost: 100,
      costImpact: -200,
      batchQuantityBefore: 10,
      batchQuantityAfter: 8,
      inventoryQuantityBefore: 20,
      inventoryQuantityAfter: 18,
      stockTransactionId: 'tx-1',
      performedAt: '2026-05-05T08:30:00.000Z',
    });

    const refreshHistoryReq = httpMock.expectOne(
      (req) => req.url === `${API_BASE_URL}/inventory/adjustments`,
    );
    expect(refreshHistoryReq.request.params.get('pageNumber')).toBe('1');
    refreshHistoryReq.flush({ items: [adjustment], totalCount: 1, pageNumber: 1, pageSize: 20 });

    const refreshBatchesReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches`);
    refreshBatchesReq.flush(batches);
    expect(component.isAdjustmentDialogOpen()).toBe(false);
  });
});
