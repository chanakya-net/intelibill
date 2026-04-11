import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { MessageService } from 'primeng/api';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { InventoryBatchesListPageComponent } from './inventory-batches-list-page.component';
import { InventoryService, InventoryBatchDto } from '../../services/inventory.service';
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
      providers: [
        MessageService,
        { provide: SuppliersFacade, useValue: suppliersFacadeMock },
      ],
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

  it('should send void request when onVoidBatch is called', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`).flush(mockBatches);

    component.onVoidBatch('b1');

    const voidReq = httpMock.expectOne(`${API_BASE_URL}/inventory/batches/b1/void`);
    expect(voidReq.request.method).toBe('POST');
    voidReq.flush({});

    httpMock.expectOne(`${API_BASE_URL}/inventory/batches`);
  });
});
