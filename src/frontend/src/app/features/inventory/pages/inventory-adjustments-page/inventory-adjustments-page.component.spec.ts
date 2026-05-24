import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import { API_BASE_URL } from '../../../../core/auth/auth.constants';
import {
  InventoryAdjustmentHistoryItem,
  InventoryBatchDto,
} from '../../services/inventory.models';
import { InventoryAdjustmentsPageComponent } from './inventory-adjustments-page.component';

describe('InventoryAdjustmentsPageComponent', () => {
  let component: InventoryAdjustmentsPageComponent;
  let fixture: ComponentFixture<InventoryAdjustmentsPageComponent>;
  let httpMock: HttpTestingController;
  const sessionSignal = signal({
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
    rememberMe: true,
    user: {
      id: 'owner-1',
      email: 'owner@test.com',
      phoneNumber: null,
      firstName: 'Owner',
      lastName: 'User',
    },
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
  });

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
    sessionSignal.set({
      ...sessionSignal(),
      activeShopId: 'shop-1',
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
    });

    await TestBed.configureTestingModule({
      imports: [
        InventoryAdjustmentsPageComponent,
        HttpClientTestingModule,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [{ provide: AuthService, useValue: { session: sessionSignal } }],
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

  it('allows void action only for owners on active adjustments', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.canVoidAdjustment(adjustment)).toBe(true);
    expect(component.canVoidAdjustment({ ...adjustment, isVoided: true })).toBe(false);

    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null }],
    });
    expect(component.canVoidAdjustment(adjustment)).toBe(false);

    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    });
    expect(component.canVoidAdjustment(adjustment)).toBe(false);
  });

  it('allows staff to view adjustment history without create or void actions', () => {
    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Staff', isDefault: true, lastUsedAt: null }],
    });

    fixture.detectChanges();
    flushInitialLoad();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(component.adjustments()).toHaveLength(1);
    expect(component.canCreateAdjustments()).toBe(false);
    expect(component.canVoidAdjustment(adjustment)).toBe(false);
    expect(host.querySelector('[data-testid="new-adjustment-action"]')).toBeNull();
    expect(host.querySelector('[data-testid="void-adjustment-action"]')).toBeNull();

    component.openNewAdjustment();
    expect(component.isAdjustmentDialogOpen()).toBe(false);
  });

  it('requires a reason before voiding an adjustment', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.onOpenVoidAdjustment(adjustment);
    expect(component.isVoidDialogOpen()).toBe(true);
    expect(component.selectedAdjustment()).toEqual(adjustment);

    component.voidForm.controls.reason.setValue('   ');
    component.onSaveVoidAdjustment();

    expect(component.voidForm.invalid).toBe(true);
    httpMock.expectNone(`${API_BASE_URL}/inventory/adjustments/adjustment-1/void`);
  });

  it('voids an adjustment and refreshes history with void metadata', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.onOpenVoidAdjustment(adjustment);
    component.voidForm.controls.reason.setValue('Duplicate stock count');
    component.onSaveVoidAdjustment();

    const voidReq = httpMock.expectOne(`${API_BASE_URL}/inventory/adjustments/adjustment-1/void`);
    expect(voidReq.request.method).toBe('POST');
    expect(voidReq.request.body).toEqual({ reason: 'Duplicate stock count' });
    voidReq.flush({
      adjustmentId: 'adjustment-1',
      reversalStockTransactionId: 'tx-reversal-1',
      batchQuantityBefore: 8,
      batchQuantityAfter: 10,
      inventoryQuantityBefore: 18,
      inventoryQuantityAfter: 20,
      voidedAt: '2026-05-05T09:00:00.000Z',
    });

    const voidedAdjustment: InventoryAdjustmentHistoryItem = {
      ...adjustment,
      isVoided: true,
      voidedAt: '2026-05-05T09:00:00.000Z',
      voidedByUserId: 'owner-1',
      voidedByDisplayName: 'Owner User',
      voidReason: 'Duplicate stock count',
      reversalStockTransactionId: 'tx-reversal-1',
    };
    const refreshHistoryReq = httpMock.expectOne(
      (req) => req.url === `${API_BASE_URL}/inventory/adjustments`,
    );
    expect(refreshHistoryReq.request.params.get('pageNumber')).toBe('1');
    refreshHistoryReq.flush({
      items: [voidedAdjustment],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
    });

    expect(component.isVoidDialogOpen()).toBe(false);
    expect(component.adjustments()[0].isVoided).toBe(true);
    expect(component.adjustments()[0].voidReason).toBe('Duplicate stock count');
    expect(component.adjustments()[0].reversalStockTransactionId).toBe('tx-reversal-1');
  });

  it('shows backend detail when voiding an adjustment fails', () => {
    fixture.detectChanges();
    flushInitialLoad();
    const messageService = fixture.debugElement.injector.get(MessageService);
    const addSpy = vi.spyOn(messageService, 'add');

    component.onOpenVoidAdjustment(adjustment);
    component.voidForm.controls.reason.setValue('Duplicate stock count');
    component.onSaveVoidAdjustment();

    const voidReq = httpMock.expectOne(`${API_BASE_URL}/inventory/adjustments/adjustment-1/void`);
    voidReq.flush(
      { detail: 'Adjustment has already been voided.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.isVoidDialogOpen()).toBe(true);
    expect(component.voidSaving()).toBe(false);
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        severity: 'error',
        detail: 'Adjustment has already been voided.',
      }),
    );
  });

  it('renders owner void action and void metadata', () => {
    fixture.detectChanges();
    flushInitialLoad();
    component.adjustments.set([
      adjustment,
      {
        ...adjustment,
        adjustmentId: 'adjustment-voided',
        adjustmentNumber: 'ADJ-0002',
        isVoided: true,
        voidedAt: '2026-05-05T09:00:00.000Z',
        voidedByUserId: 'owner-1',
        voidedByDisplayName: 'Owner User',
        voidReason: 'Duplicate stock count',
        reversalStockTransactionId: 'tx-reversal-1',
      },
    ]);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('[data-testid="void-adjustment-action"]')).toHaveLength(2);
    expect(host.textContent).toContain('Owner User');
    expect(host.textContent).toContain('Duplicate stock count');
    expect(host.textContent).toContain('tx-reversal-1');

    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null }],
    });
    fixture.detectChanges();

    expect(host.querySelectorAll('[data-testid="void-adjustment-action"]')).toHaveLength(0);
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
