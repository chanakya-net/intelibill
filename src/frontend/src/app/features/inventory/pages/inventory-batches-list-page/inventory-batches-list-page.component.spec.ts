import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { InventoryBatchesListPageComponent } from './inventory-batches-list-page.component';
import { InventoryService } from '../../services/inventory.service';
import type { InventoryBatchDto } from '../../services/inventory.models';
import { SuppliersFacade } from '../../../suppliers/state/suppliers.facade';
import { API_BASE_URL } from '../../../../core/auth/auth.constants';

describe('InventoryBatchesListPageComponent', () => {
  let component: InventoryBatchesListPageComponent;
  let fixture: ComponentFixture<InventoryBatchesListPageComponent>;
  let httpMock: HttpTestingController;

  const mockBatches: InventoryBatchDto[] = [
    {
      id: 'b1',
      shopId: 's1',
      itemId: 'i1',
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
      createdAt: new Date().toISOString(),
      updatedAt: null,
    },
  ];

  beforeEach(async () => {
    const suppliersFacadeMock = {
      suppliers: () => [],
      load: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [
        InventoryBatchesListPageComponent,
        HttpClientTestingModule,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({
          langs: {},
          preloadLangs: true,
        }),
      ],
      providers: [MessageService, { provide: SuppliersFacade, useValue: suppliersFacadeMock }],
    }).compileComponents();

    fixture = TestBed.createComponent(InventoryBatchesListPageComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should load batches on init', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne(`${API_BASE_URL}/inventory/batches`);
    expect(req.request.method).toBe('GET');
    req.flush(mockBatches);

    expect(component.batches().length).toBe(1);
    expect(component.batches()[0].itemName).toBe('Rice');
  });

  it('should open edit dialog with populated form', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onEditBatch(mockBatches[0]);

    expect(component.isEditDialogOpen()).toBe(true);
    expect(component.selectedBatch()?.id).toBe('b1');
    expect(component.editForm.value.quantity).toBe(10);
    expect(component.editForm.value.newBatchNumber).toBe('');
  });

  it('should open adjustment dialog with default decrease form values', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onAdjustBatch(mockBatches[0]);

    expect(component.isAdjustmentDialogOpen()).toBe(true);
    expect(component.selectedBatch()?.id).toBe('b1');
    expect(component.adjustmentForm.getRawValue()).toMatchObject({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 1,
      performedAt: null,
      notes: '',
    });
  });

  it('should ignore adjustment attempts for voided batches', () => {
    const voidedBatch: InventoryBatchDto = {
      ...mockBatches[0],
      id: 'b2',
      batchNumber: 'BATCH-VOID',
      isVoided: true,
    };

    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onAdjustBatch(voidedBatch);

    expect(component.isAdjustmentDialogOpen()).toBe(false);
    expect(component.selectedBatch()).toBeNull();
  });

  it('should filter adjustment reasons by direction', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onAdjustBatch(mockBatches[0]);

    component.adjustmentForm.controls.direction.setValue('Decrease');
    expect(component.adjustmentReasonOptions().map((option) => option.value)).toContain('Damaged');
    expect(component.adjustmentReasonOptions().map((option) => option.value)).not.toContain(
      'FoundStock',
    );

    component.adjustmentForm.controls.direction.setValue('Increase');
    expect(component.adjustmentReasonOptions().map((option) => option.value)).toContain(
      'FoundStock',
    );
    expect(component.adjustmentReasonOptions().map((option) => option.value)).not.toContain(
      'Damaged',
    );
  });

  it('should require notes for other gain or loss adjustment reasons', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onAdjustBatch(mockBatches[0]);
    component.adjustmentForm.patchValue({
      direction: 'Decrease',
      reason: 'OtherLoss',
      quantity: 1,
      notes: '',
    });

    expect(component.adjustmentForm.controls.notes.hasError('required')).toBe(true);
    expect(component.adjustmentForm.invalid).toBe(true);

    component.adjustmentForm.controls.notes.setValue('Lost during count');
    expect(component.adjustmentForm.controls.notes.hasError('required')).toBe(false);
  });

  it('should preview adjusted quantity and cost impact', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onAdjustBatch(mockBatches[0]);
    component.adjustmentForm.patchValue({
      direction: 'Decrease',
      quantity: 2.5,
    });

    expect(component.adjustmentPreview()).toEqual({
      batchQuantityBefore: 10,
      batchQuantityAfter: 7.5,
      unitCost: 100,
      costImpact: -250,
    });

    component.adjustmentForm.patchValue({
      direction: 'Increase',
      quantity: 3,
    });

    expect(component.adjustmentPreview()).toEqual({
      batchQuantityBefore: 10,
      batchQuantityAfter: 13,
      unitCost: 100,
      costImpact: 300,
    });
  });

  it('should send correction request and reload batches on save', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onEditBatch(mockBatches[0]);
    component.editForm.patchValue({
      quantity: 15,
      newBatchNumber: 'CORRECTED-001',
    });

    component.onSaveEdit();

    const editReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches/b1`);
    expect(editReq.request.method).toBe('PUT');
    expect(editReq.request.body.newBatchNumber).toBe('CORRECTED-001');
    expect(editReq.request.body.quantity).toBe(15);
    editReq.flush({});

    // Should reload
    const reloadReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches`);
    expect(reloadReq.request.method).toBe('GET');
    expect(component.isEditDialogOpen()).toBe(false);
  });

  it('should send adjustment request and reload batches on success', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onAdjustBatch(mockBatches[0]);
    component.adjustmentForm.patchValue({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 2,
      performedAt: new Date('2026-05-05T08:30'),
      notes: 'Damaged while unloading',
    });

    component.onSaveAdjustment();

    const adjustReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches/b1/adjust`);
    expect(adjustReq.request.method).toBe('POST');
    expect(adjustReq.request.body).toEqual({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 2,
      performedAt: new Date('2026-05-05T08:30').toISOString(),
      notes: 'Damaged while unloading',
    });
    adjustReq.flush({
      adjustmentId: 'adjustment-1',
      adjustmentNumber: 'ADJ-0001',
      quantity: 2,
      unitCost: 100,
      costImpact: -200,
      batchQuantityBefore: 10,
      batchQuantityAfter: 8,
      inventoryQuantityBefore: 20,
      inventoryQuantityAfter: 18,
      stockTransactionId: 'tx-1',
      performedAt: '2026-05-05T03:00:00.000Z',
    });

    const reloadReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches`);
    expect(reloadReq.request.method).toBe('GET');
    expect(component.isAdjustmentDialogOpen()).toBe(false);
  });

  it('should send void request when onVoidBatch is called', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onVoidBatch('b1');

    const voidReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches/b1/void`);
    expect(voidReq.request.method).toBe('POST');
    voidReq.flush({});

    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`);
  });

  it('should prepare selected batches for barcode label printing with batch quantities', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onBatchSelectionChange(['b1']);
    component.onPrintSelectedBatches();

    expect(component.barcodeLabelPrintDialogVisible()).toBe(true);
    expect(component.barcodeLabelPrintCandidates()).toEqual([
      {
        itemId: 'i1',
        itemName: 'Rice',
        barcode: '111',
        inventoryBatchId: 'b1',
        quantity: 10,
      },
    ]);
  });

  it('should prepare a single batch row for label printing from the table action', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onBatchTableAction({ action: 'printLabels', batchId: 'b1' });

    expect(component.barcodeLabelPrintDialogVisible()).toBe(true);
    expect(component.barcodeLabelPrintCandidates()).toEqual([
      {
        itemId: 'i1',
        itemName: 'Rice',
        barcode: '111',
        inventoryBatchId: 'b1',
        quantity: 10,
      },
    ]);
  });

  it('should open and download the PDF returned by the print API', async () => {
    const openSpy = vi.spyOn(window, 'open').mockReturnValue({} as Window);
    const createSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');

    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onBarcodeLabelPrintRequested({
      items: [{ itemId: 'i1', quantity: 10, inventoryBatchId: 'b1' }],
    });

    const printReq = httpMock.expectOne(`${API_BASE_URL}/items/barcodes/labels`);
    expect(printReq.request.method).toBe('POST');
    expect(printReq.request.body).toEqual({
      items: [{ itemId: 'i1', quantity: 10, inventoryBatchId: 'b1' }],
    });
    expect(printReq.request.responseType).toBe('blob');

    printReq.flush(new Blob(['pdf-data'], { type: 'application/pdf' }), {
      status: 200,
      statusText: 'OK',
    });

    expect(createSpy).toHaveBeenCalledTimes(1);
    expect(openSpy).toHaveBeenCalledWith('blob:mock-url', '_blank');
  });
});
