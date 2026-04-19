import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SALE_ENDPOINTS } from '../../../core/auth/auth.constants';
import { SaleService, SaleDto, SaleListItemDto } from './sale.service';

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
      paymentMethod: 1,
      soldAt: new Date().toISOString(),
      totalAmount: 500,
      totalTaxAmount: 50,
      customerName: 'John',
      customerPhone: null,
      itemCount: 2,
    },
  ];

  const makeSaleDto = (): SaleDto => ({
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    paymentMethod: 1,
    soldAt: new Date().toISOString(),
    totalAmount: 500,
    totalTaxAmount: 50,
    items: [],
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
      items: [{ barcode: 'BC-001', batchNumber: 'B-01', itemName: 'Item 1', quantity: 2, costPrice: 80, salesPrice: 100, mrp: 120, taxRatePercent: 18, isPriceIncludingTax: false }],
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
});
