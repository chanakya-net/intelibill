import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import { SaleService } from './sale.service';
import type {
  SaleDto,
  SaleListItemDto,
  ProfitLossAppliedFiltersDto,
  ProfitLossReportItemDto,
  ProfitLossReportQueryParams,
  ProfitLossReportResultDto,
  ProfitLossSummaryDto,
  PreviewSaleRequest,
  SalesHistoryQueryParams,
  SalesHistoryResultDto,
  SalesHistorySummaryDto,
  SalePreviewDto,
} from './sale.models';

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

  const makeSaleDto = (): SaleDto => ({
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    customerId: null,
    customerName: null,
    customerPhone: null,
    paymentMethod: 1,
    soldAt: new Date().toISOString(),
    paidAmount: 500,
    dueAmount: 0,
    totalBeforeDiscount: 500,
    totalDiscountAmount: 0,
    totalAmount: 500,
    totalTaxAmount: 50,
    items: [],
    returns: [],
    warnings: [],
  });

  it('sends GET request to list endpoint with history query params and maps response', () => {
    const { service, http } = setup();
    const queryParams: SalesHistoryQueryParams = {
      from: '2026-05-01',
      to: '2026-05-12',
      search: 'john',
      status: 'refunded',
      page: 2,
      pageSize: 25,
    };
    const sales: SaleListItemDto[] = [
      {
        saleId: 'sale-1',
        invoiceNumber: 'INV-001',
        customerId: null,
        paymentMethod: 1,
        soldAt: '2026-05-11T00:00:00.000Z',
        paidAmount: 500,
        dueAmount: 0,
        totalBeforeDiscount: 500,
        totalDiscountAmount: 0,
        totalAmount: 500,
        totalTaxAmount: 50,
        customerName: 'John',
        customerPhone: null,
        itemCount: 2,
        returnNumbers: [],
        status: 'partiallyPaid',
        refundAmount: 0,
        dueReductionAmount: 0,
      },
      {
        saleId: 'sale-2',
        invoiceNumber: 'INV-002',
        customerId: 'cust-1',
        paymentMethod: 1,
        soldAt: '2026-05-11T01:00:00.000Z',
        paidAmount: 495,
        dueAmount: 0,
        totalBeforeDiscount: 550,
        totalDiscountAmount: 55,
        totalAmount: 495,
        totalTaxAmount: 45,
        customerName: 'Jane',
        customerPhone: '9999999999',
        itemCount: 1,
        returnNumbers: ['RET-001'],
        status: 'paid',
        refundAmount: 25,
        dueReductionAmount: 5,
      },
    ];
    const summary: SalesHistorySummaryDto = {
      periodSales: 995,
      invoiceCount: 2,
      refundAmount: 25,
    };
    const result: SalesHistoryResultDto = {
      items: sales,
      totalCount: 2,
      pageNumber: 2,
      pageSize: 25,
      summary,
    };

    service.getSales(queryParams).subscribe((response) => {
      expect(response.items).toHaveLength(2);
      expect(response.items[0].totalBeforeDiscount).toBe(500);
      expect(response.items[0].totalDiscountAmount).toBe(0);
      expect(response.items[1].totalBeforeDiscount).toBe(550);
      expect(response.items[1].totalDiscountAmount).toBe(55);
      expect(response.totalCount).toBe(2);
      expect(response.pageNumber).toBe(2);
      expect(response.pageSize).toBe(25);
      expect(response.summary).toEqual(summary);
    });

    const req = http.expectOne((request) => request.url === SALE_ENDPOINTS.list);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-05-01');
    expect(req.request.params.get('to')).toBe('2026-05-12');
    expect(req.request.params.get('search')).toBe('john');
    expect(req.request.params.get('status')).toBe('refunded');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush(result);
    http.verify();
  });

  it('sends GET request to detail endpoint', () => {
    const { service, http } = setup();
    const sale = makeSaleDto();

    service.getSaleById('sale-1').subscribe((result) => {
      expect(result.saleId).toBe('sale-1');
      expect(result.invoiceNumber).toBe('INV-001');
      expect(result.totalBeforeDiscount).toBe(500);
      expect(result.totalDiscountAmount).toBe(0);
    });

    const req = http.expectOne(SALE_ENDPOINTS.detail('sale-1'));
    expect(req.request.method).toBe('GET');
    req.flush(sale);
    http.verify();
  });

  it('maps discounted sale detail fields from the API payload', () => {
    const { service, http } = setup();
  const sale: SaleDto = {
      saleId: 'sale-2',
      invoiceNumber: 'INV-002',
      customerId: 'cust-1',
      customerName: 'Jane',
      customerPhone: '9999999999',
      paymentMethod: 1,
      soldAt: '2026-05-11T01:00:00.000Z',
      paidAmount: 495,
      dueAmount: 0,
      totalBeforeDiscount: 550,
      totalDiscountAmount: 55,
      totalAmount: 495,
      totalTaxAmount: 45,
      items: [
        {
          saleItemId: 'line-1',
          lineType: 'Goods',
          itemId: 'item-1',
          serviceId: null,
          itemName: 'Rice',
          lineCode: 'BC-001',
          inventoryBatchId: 'batch-1',
          quantity: 5,
          salesPrice: 100,
          originalSalesPrice: 100,
          finalSalesPrice: 100,
          preTaxAmountBeforeDiscount: 500,
          itemDiscountAmount: 20,
          saleDiscountAmount: 35,
          taxableAmount: 445,
          taxAmount: 45,
          totalAmount: 490,
          savingsAmount: 55,
          taxRatePercent: 10,
          isPriceIncludingTax: false,
          hasPriceMismatch: false,
          hsnCode: null,
          returnedQuantity: 0,
          returnableQuantity: 5,
          returnStatus: 'NotReturned',
        },
      ],
      returns: [],
      warnings: [],
    };

    service.getSaleById('sale-2').subscribe((result) => {
      expect(result.totalBeforeDiscount).toBe(550);
      expect(result.totalDiscountAmount).toBe(55);
      expect(result.items[0].originalSalesPrice).toBe(100);
      expect(result.items[0].finalSalesPrice).toBe(100);
      expect(result.items[0].itemDiscountAmount).toBe(20);
      expect(result.items[0].saleDiscountAmount).toBe(35);
      expect(result.items[0].taxAmount).toBe(45);
      expect(result.items[0].totalAmount).toBe(490);
      expect(result.items[0].savingsAmount).toBe(55);
    });

    const req = http.expectOne(SALE_ENDPOINTS.detail('sale-2'));
    expect(req.request.method).toBe('GET');
    req.flush(sale);
    http.verify();
  });

  it('sends POST request to record endpoint', () => {
    const { service, http } = setup();
    const sale = makeSaleDto();
    const payload = {
      idempotencyKey: 'sale-test-key',
      customerId: null,
      customerName: 'Walk-in',
      customerPhone: null,
      paymentMethod: 1,
      paidAmount: 200,
      dueAmount: 0,
      saleDiscount: { type: 0 as const, value: 0 },
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
        clientLineKey: 'clk-uuid-001',
        itemDiscount: { type: 0 as const, value: 0 },
        hsnCode: '0902',
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

  it('loads profit loss report with query params and result metadata', () => {
    const { service, http } = setup();
    const queryParams: ProfitLossReportQueryParams = {
      from: '2026-05-01',
      to: '2026-05-31',
      type: 'saleReturn',
      search: 'INV',
      page: 3,
      pageSize: 15,
    };
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
        marginPercent: 25,
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
        marginPercent: null,
        rowType: 'InventoryAdjustment',
        inventoryAdjustmentId: 'adjustment-1',
      },
    ];
    const summary: ProfitLossSummaryDto = {
      netProfitAfterTax: -60,
      revenueIncludingTax: 118,
      totalCost: 80,
      averageMarginPercent: -75,
      invoiceCount: 1,
      returnCount: 0,
      adjustmentCount: 1,
    };
    const appliedFilters: ProfitLossAppliedFiltersDto = {
      from: '2026-05-01',
      to: '2026-05-31',
      type: 'saleReturn',
      search: 'INV',
      pageNumber: 3,
      pageSize: 15,
    };
    const result: ProfitLossReportResultDto = {
      items: report,
      totalCount: 2,
      pageNumber: 3,
      pageSize: 15,
      summary,
      appliedFilters,
    };

    service.getProfitLossReport(queryParams).subscribe((response) => {
      expect(response.items[0].referenceNumber).toBe('INV-001');
      expect(response.items[0].rowType).toBe('Sale');
      expect(response.items[0].inventoryAdjustmentId).toBeNull();
      expect(response.items[0].marginPercent).toBe(25);
      expect(response.items[1].saleId).toBeNull();
      expect(response.items[1].rowType).toBe('InventoryAdjustment');
      expect(response.items[1].inventoryAdjustmentId).toBe('adjustment-1');
      expect(response.totalCount).toBe(2);
      expect(response.pageNumber).toBe(3);
      expect(response.pageSize).toBe(15);
      expect(response.summary).toEqual(summary);
      expect(response.appliedFilters).toEqual(appliedFilters);
    });

    const req = http.expectOne((request) => request.url === SALE_ENDPOINTS.profitLoss);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-05-01');
    expect(req.request.params.get('to')).toBe('2026-05-31');
    expect(req.request.params.get('type')).toBe('saleReturn');
    expect(req.request.params.get('search')).toBe('INV');
    expect(req.request.params.get('page')).toBe('3');
    expect(req.request.params.get('pageSize')).toBe('15');
    req.flush(result);
    http.verify();
  });

  it('sends POST request to preview endpoint', () => {
    const { service, http } = setup();
    const payload: PreviewSaleRequest = {
      saleDiscount: { type: 0, value: 0 },
      items: [
        {
          inventoryBatchId: 'batch-1',
          barcode: 'BC-001',
          batchNumber: 'B-01',
          itemName: 'Item 1',
          quantity: 2,
          costPrice: 80,
          salesPrice: 100,
          mrp: 120,
          taxRatePercent: 18,
          isPriceIncludingTax: false,
          itemDiscount: { type: 0, value: 0 },
          clientLineKey: 'clk-uuid-001',
          hsnCode: '0902',
        },
      ],
    };
    const preview: SalePreviewDto = {
      totalAmount: 236,
      totalTaxableAmount: 200,
      totalTaxAmount: 36,
      totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 200,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    };

    service.previewSale(payload).subscribe((result) => {
      expect(result.totalAmount).toBe(236);
      expect(result.totalTaxAmount).toBe(36);
    });

    const req = http.expectOne(SALE_ENDPOINTS.preview);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(preview);
    http.verify();
  });

  it('sends POST request to reserve invoice lease endpoint', () => {
    const { service, http } = setup();
    const payload = { deviceId: 'device-1' };

    service.reserveInvoiceLease(payload).subscribe((result) => {
      expect(result.leaseId).toBe('lease-1');
      expect(result.remainingCount).toBe(200);
    });

    const req = http.expectOne(SALE_ENDPOINTS.reserveInvoiceLease);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({
      leaseId: 'lease-1',
      shopId: 'shop-1',
      deviceId: 'device-1',
      fiscalYear: '2026-27',
      prefix: 'INV-',
      numberPadding: 6,
      rangeStart: 1,
      rangeEnd: 200,
      nextNumber: 1,
      remainingCount: 200,
      reservedAt: '2026-05-22T00:00:00.000Z',
      expiresAt: '2026-05-29T00:00:00.000Z',
    });
    http.verify();
  });

  it('sends POST request to offline sync endpoint', () => {
    const { service, http } = setup();
    const payload = {
      deviceId: 'device-1',
      sales: [
        {
          clientSaleId: 'client-1',
          invoiceNumber: 'INV-1',
          soldAt: '2026-05-22T10:00:00.000Z',
          customerId: null,
          customerName: null,
          customerPhone: null,
          paymentMethod: 1,
          paidAmount: 100,
          dueAmount: 0,
          subtotalBeforeDiscount: 100,
          totalBeforeDiscount: 100,
          totalDiscountAmount: 0,
          totalTaxAmount: 0,
          totalAmount: 100,
          saleDiscountOverrideType: 0,
          saleDiscountOverrideValue: 0,
          configuredSaleRuleId: null,
          configuredSaleRuleType: null,
          configuredSaleRulePercentage: null,
          configuredSaleRuleThresholdAmount: null,
          items: [],
        },
      ],
    };

    service.syncOfflineSales(payload).subscribe((result) => {
      expect(result.results[0].clientSaleId).toBe('client-1');
      expect(result.results[0].status).toBe('Synced');
    });

    const req = http.expectOne(SALE_ENDPOINTS.offlineSync);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({
      results: [
        {
          clientSaleId: 'client-1',
          status: 'Synced',
          saleId: 'server-sale-1',
          invoiceNumber: 'INV-1',
          errors: [],
          warnings: [],
        },
      ],
    });
    http.verify();
  });

  it('sends GET request to P&L endpoint with query params and maps response', () => {
    const { service, http } = setup();
    const queryParams = {
      from: '2026-05-01',
      to: '2026-05-12',
      type: 'sale' as const,
      search: 'rice',
      page: 1,
      pageSize: 50,
    };
    const items: ProfitLossReportItemDto[] = [
      {
        saleId: 'sale-1',
        referenceNumber: 'INV-001',
        occurredAt: '2026-05-11T00:00:00.000Z',
        partyName: 'John',
        totalCost: 300,
        wastageCost: 0,
        revenueBeforeTax: 500,
        revenueAfterTax: 550,
        profitBeforeTax: 200,
        profitAfterTax: 250,
        rowType: 'Sale',
        inventoryAdjustmentId: null,
        marginPercent: 40,
      },
    ];
    const result = {
      items,
      summary: {
        netProfitAfterTax: 250,
        revenueIncludingTax: 550,
        totalCost: 300,
        averageMarginPercent: 83.33,
        invoiceCount: 1,
        returnCount: 0,
        adjustmentCount: 0,
      },
      appliedFilters: {
        from: '2026-05-01',
        to: '2026-05-12',
        type: 'sale',
        search: 'rice',
        pageNumber: 1,
        pageSize: 50,
      },
      totalCount: 1,
      pageNumber: 1,
      pageSize: 50,
    };

    service.getProfitLossReport(queryParams).subscribe((response) => {
      expect(response.items).toHaveLength(1);
      expect(response.items[0].referenceNumber).toBe('INV-001');
      expect(response.items[0].marginPercent).toBe(40);
      expect(response.summary.netProfitAfterTax).toBe(250);
      expect(response.appliedFilters.type).toBe('sale');
      expect(response.totalCount).toBe(1);
      expect(response.pageNumber).toBe(1);
      expect(response.pageSize).toBe(50);
    });

    const req = http.expectOne((request) => request.url === SALE_ENDPOINTS.profitLoss);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-05-01');
    expect(req.request.params.get('to')).toBe('2026-05-12');
    expect(req.request.params.get('type')).toBe('sale');
    expect(req.request.params.get('search')).toBe('rice');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('50');
    req.flush(result);
    http.verify();
  });

  it('sends GET request to P&L endpoint with partial query params', () => {
    const { service, http } = setup();
    const queryParams = {
      from: '2026-05-01',
      page: 2,
    };
    const result = {
      items: [],
      summary: {
        netProfitAfterTax: 0,
        revenueIncludingTax: 0,
        totalCost: 0,
        averageMarginPercent: null,
        invoiceCount: 0,
        returnCount: 0,
        adjustmentCount: 0,
      },
      appliedFilters: {
        from: '2026-05-01',
        to: '2026-05-30',
        type: 'all',
        search: null,
        pageNumber: 2,
        pageSize: 20,
      },
      totalCount: 0,
      pageNumber: 2,
      pageSize: 20,
    };

    service.getProfitLossReport(queryParams).subscribe((response) => {
      expect(response.items).toHaveLength(0);
      expect(response.pageNumber).toBe(2);
    });

    const req = http.expectOne((request) => request.url === SALE_ENDPOINTS.profitLoss);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-05-01');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('to')).toBeNull();
    expect(req.request.params.get('type')).toBeNull();
    expect(req.request.params.get('search')).toBeNull();
    expect(req.request.params.get('pageSize')).toBeNull();
    req.flush(result);
    http.verify();
  });

  it('sends GET request to P&L endpoint without params', () => {
    const { service, http } = setup();
    const result = {
      items: [],
      summary: {
        netProfitAfterTax: 0,
        revenueIncludingTax: 0,
        totalCost: 0,
        averageMarginPercent: null,
        invoiceCount: 0,
        returnCount: 0,
        adjustmentCount: 0,
      },
      appliedFilters: {
        from: '2026-05-24',
        to: '2026-05-30',
        type: 'all',
        search: null,
        pageNumber: 1,
        pageSize: 20,
      },
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
    };

    service.getProfitLossReport().subscribe((response) => {
      expect(response.items).toHaveLength(0);
      expect(response.pageNumber).toBe(1);
    });

    const req = http.expectOne((request) => request.url === SALE_ENDPOINTS.profitLoss);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush(result);
    http.verify();
  });
});
