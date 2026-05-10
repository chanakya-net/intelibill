import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import { SaleService, SaleDto, SaleListItemDto, ProfitLossReportItemDto } from './sale.service';

describe('SaleService', () => {
  function setup(): { service: SaleService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    return {
      service: TestBed.inject(SaleService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  const makeSaleList = (): SaleListItemDto[] => [
    {
      saleId: 'sale-1',
      invoiceNumber: 'INV-001',
      customerId: null,
      paymentMethod: 1,
      soldAt: new Date().toISOString(),
      paidAmount: 500,
      dueAmount: 0,
      totalAmount: 500,
      totalTaxAmount: 50,
      customerName: 'John',
      customerPhone: null,
      itemCount: 2,
      returnNumbers: [],
    },
  ];

  const makeSaleDto = (): SaleDto => ({
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    customerId: null,
    paymentMethod: 1,
    soldAt: new Date().toISOString(),
    paidAmount: 500,
    dueAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    items: [],
    returns: [],
    warnings: [],
  });

  it('sends GET request to list endpoint', () => {
    const { service, http } = setup();
    const sales = makeSaleList();

    service.getSales().subscribe((result) => {
      expect(result).toHaveLength(1);
      expect(result[0].saleId).toBe('sale-1');
    });

    const req = http.expectOne(SALE_ENDPOINTS.list);
    expect(req.request.method).toBe('GET');
    req.flush(sales);
    http.verify();
  });

  it('sends GET request to detail endpoint', () => {
    const { service, http } = setup();
    const sale = makeSaleDto();

    service.getSaleById('sale-1').subscribe((result) => {
      expect(result.saleId).toBe('sale-1');
      expect(result.invoiceNumber).toBe('INV-001');
    });

    const req = http.expectOne(SALE_ENDPOINTS.detail('sale-1'));
    expect(req.request.method).toBe('GET');
    req.flush(sale);
    http.verify();
  });

  it('sends POST request to record endpoint', () => {
    const { service, http } = setup();
    const sale = makeSaleDto();
    const payload = {
      customerId: null,
      customerName: 'Walk-in',
      customerPhone: null,
      paymentMethod: 1,
      paidAmount: 200,
      dueAmount: 0,
      items: [{
        barcode: 'BC-001',
        batchNumber: 'B-01',
        itemName: 'Item 1',
        quantity: 2,
        costPrice: 80,
        salesPrice: 100,
        mrp: 120,
        taxRatePercent: 18,
        isPriceIncludingTax: false,
        inventoryBatchId: 'batch-1',
        clientLineKey: 'batch-1',
      }],
    };

    service.recordSale(payload).subscribe((result) => {
      expect(result.saleId).toBe('sale-1');
    });

    const req = http.expectOne(SALE_ENDPOINTS.record);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(sale);
    http.verify();
  });

  it('sends POST request to return preview endpoint', () => {
    const { service, http } = setup();
    const payload = {
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
      items: [{ saleItemId: 'line-1', quantity: 1, condition: 1 as const, approvedRefundAmount: 100, notes: null }],
    };
    const preview = {
      saleId: 'sale-1',
      hasFinancialAccess: true,
      lines: [],
      financial: null,
      warnings: [],
    };

    service.previewSaleReturn('sale-1', payload).subscribe((result) => {
      expect(result.saleId).toBe('sale-1');
    });

    const req = http.expectOne(`${SALE_ENDPOINTS.detail('sale-1')}/returns/preview`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(preview);
    http.verify();
  });

  it('sends POST request to record return endpoint', () => {
    const { service, http } = setup();
    const sale = makeSaleDto();
    const payload = {
      payoutMethod: 1,
      dueReductionOverrideAmount: null,
      dueOverrideReason: null,
      notes: null,
      items: [{ saleItemId: 'line-1', quantity: 1, condition: 1 as const, approvedRefundAmount: 100, notes: 'Sealed' }],
    };

    service.recordSaleReturn('sale-1', payload).subscribe((result) => {
      expect(result.saleId).toBe('sale-1');
    });

    const req = http.expectOne(`${SALE_ENDPOINTS.detail('sale-1')}/returns`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(sale);
    http.verify();
  });

  it('sends POST request to void return endpoint', () => {
    const { service, http } = setup();
    const payload = { reason: 'Wrong return recorded' };

    service.voidSaleReturn('return-1', payload).subscribe();

    const req = http.expectOne(`${SALE_ENDPOINTS.record}/returns/return-1/void`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(null);
    http.verify();
  });

  it('loads profit loss report with neutral row fields', () => {
    const { service, http } = setup();
    const report: ProfitLossReportItemDto[] = [
      {
        saleId: 'sale-1',
        referenceNumber: 'INV-001',
        occurredAt: new Date().toISOString(),
        partyName: 'John',
        totalCost: 80,
        wastageCost: 0,
        revenueBeforeTax: 100,
        revenueAfterTax: 118,
        profitBeforeTax: 38,
        profitAfterTax: 20,
        rowType: 'Sale',
        inventoryAdjustmentId: null,
      },
      {
        saleId: null,
        referenceNumber: 'ADJ-001',
        occurredAt: new Date().toISOString(),
        partyName: null,
        totalCost: 0,
        wastageCost: 80,
        revenueBeforeTax: 0,
        revenueAfterTax: 0,
        profitBeforeTax: -80,
        profitAfterTax: -80,
        rowType: 'InventoryAdjustment',
        inventoryAdjustmentId: 'adjustment-1',
      },
    ];

    service.getProfitLossReport().subscribe((result) => {
      expect(result[0].referenceNumber).toBe('INV-001');
      expect(result[0].rowType).toBe('Sale');
      expect(result[0].inventoryAdjustmentId).toBeNull();
      expect(result[1].saleId).toBeNull();
      expect(result[1].rowType).toBe('InventoryAdjustment');
      expect(result[1].inventoryAdjustmentId).toBe('adjustment-1');
    });

    const req = http.expectOne(SALE_ENDPOINTS.profitLoss);
    expect(req.request.method).toBe('GET');
    req.flush(report);
    http.verify();
  });
});
