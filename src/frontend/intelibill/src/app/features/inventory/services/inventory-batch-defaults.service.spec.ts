import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { describe, expect, it, beforeEach, vi } from 'vitest';

import { InventoryBatchDefaultsService } from './inventory-batch-defaults.service';
import { InventoryService } from './inventory.service';

describe('InventoryBatchDefaultsService', () => {
  const inventoryService = {
    lookupHsn: vi.fn(),
  };

  beforeEach(() => {
    inventoryService.lookupHsn.mockReset();
    TestBed.configureTestingModule({
      providers: [
        InventoryBatchDefaultsService,
        { provide: InventoryService, useValue: inventoryService },
      ],
    });
  });

  it('generates inventory batch numbers with the shared format', () => {
    const service = TestBed.inject(InventoryBatchDefaultsService);

    const batchNumber = service.generateBatchNumber(new Date(2026, 0, 2));

    expect(batchNumber).toMatch(/^BN-20260102-[A-Z2-9]{5}$/);
  });

  it('delegates HSN lookup to the inventory service', async () => {
    inventoryService.lookupHsn.mockReturnValueOnce(of({
      hsnCodes: ['0401'],
      taxScenarios: [{ condition: 'default', taxPercentage: '18%' }],
    }));
    const service = TestBed.inject(InventoryBatchDefaultsService);

    const result = await service.lookupHsn('Milk');

    expect(inventoryService.lookupHsn).toHaveBeenCalledWith('Milk');
    expect(result.hsnCodes).toEqual(['0401']);
  });

  it('returns an automatic tax rate only when one HSN tax scenario is available', () => {
    const service = TestBed.inject(InventoryBatchDefaultsService);

    expect(service.getAutoTaxRatePercent({
      hsnCodes: ['0401'],
      taxScenarios: [{ condition: 'default', taxPercentage: '18%' }],
    })).toBe(18);
    expect(service.getAutoTaxRatePercent({
      hsnCodes: ['0401'],
      taxScenarios: [
        { condition: 'condition a', taxPercentage: '5%' },
        { condition: 'condition b', taxPercentage: '12%' },
      ],
    })).toBeNull();
  });
});
