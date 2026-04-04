import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ITEM_ENDPOINTS } from '../../../core/auth/auth.constants';
import { InventoryService } from './inventory.service';

describe('InventoryService', () => {
  function setup(): { service: InventoryService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    return {
      service: TestBed.inject(InventoryService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('sends add item request to items endpoint', () => {
    const { service, http } = setup();

    service
      .addItem({
        name: 'Premium Tea',
        barcode: 'ABC123',
        description: null,
        uom: 'packet',
        isActive: true,
        preferredSupplierId: null,
      })
      .subscribe((item) => {
        expect(item.name).toBe('Premium Tea');
      });

    const request = http.expectOne(ITEM_ENDPOINTS.add);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Premium Tea',
      barcode: 'ABC123',
      description: null,
      uom: 'packet',
      isActive: true,
      preferredSupplierId: null,
    });

    request.flush({
      itemId: 'item-1',
      name: 'Premium Tea',
      barcode: 'ABC123',
      description: null,
      uom: 'packet',
      isActive: true,
      preferredSupplierId: null,
    });

    http.verify();
  });

  it('loads items from items endpoint', () => {
    const { service, http } = setup();

    service.getItems().subscribe((items) => {
      expect(items).toHaveLength(1);
      expect(items[0].name).toBe('Premium Tea');
    });

    const request = http.expectOne(ITEM_ENDPOINTS.list);
    expect(request.request.method).toBe('GET');

    request.flush([
      {
        itemId: 'item-1',
        name: 'Premium Tea',
        barcode: 'ABC123',
        description: null,
        uom: 'packet',
        isActive: true,
        preferredSupplierId: null,
      },
    ]);

    http.verify();
  });
});
