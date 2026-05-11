import { of, throwError } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { InventoryService } from '../../inventory/services/inventory.service';
import { DiscountRuleDto, DiscountRuleListItemDto, DiscountService } from '../services/discount.service';
import { DiscountsPageComponent } from './discounts-page.component';

describe('DiscountsPageComponent', () => {
  const makeListItem = (overrides: Partial<DiscountRuleListItemDto> = {}): DiscountRuleListItemDto => ({
    id: 'rule-1',
    ruleType: 'BatchPercentage',
    name: '10% off batch',
    isActive: true,
    startsAt: null,
    endsAt: null,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  });

  const makeRuleDto = (overrides: Partial<DiscountRuleDto> = {}): DiscountRuleDto => ({
    id: 'rule-1',
    ruleType: 'BatchPercentage',
    name: '10% off batch',
    description: null,
    inventoryBatchId: null,
    percentage: 10,
    thresholdAmount: null,
    startsAt: null,
    endsAt: null,
    isActive: true,
    disabledAt: null,
    disabledReason: null,
    belowCostConfirmed: false,
    belowCostConfirmationReason: null,
    replacesRuleId: null,
    replacedByRuleId: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: null,
    ...overrides,
  });

  const discountService = {
    getDiscountRules: vi.fn(),
    getDiscountRule: vi.fn(),
    disableDiscountRule: vi.fn(),
    previewDiscountRule: vi.fn(),
    createDiscountRule: vi.fn(),
    replaceDiscountRule: vi.fn(),
  };

  const inventoryService = {
    getAvailableBatchesBySearchTerm: vi.fn(),
  };

  beforeEach(() => {
    discountService.getDiscountRules.mockReset();
    discountService.getDiscountRule.mockReset();
    discountService.disableDiscountRule.mockReset();
    discountService.previewDiscountRule.mockReset();

    discountService.getDiscountRules.mockReturnValue(
      of({ items: [makeListItem()], totalCount: 1, pageNumber: 1, pageSize: 20 }),
    );
    discountService.getDiscountRule.mockReturnValue(of(makeRuleDto()));

    TestBed.configureTestingModule({
      imports: [DiscountsPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: DiscountService, useValue: discountService },
        { provide: InventoryService, useValue: inventoryService },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads list with default filters and selects first rule', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    expect(discountService.getDiscountRules).toHaveBeenCalledWith(
      expect.objectContaining({
        status: 'active',
        sort: 'created_desc',
        page: 1,
        pageSize: 20,
      }),
    );
    expect(fixture.componentInstance.selectedRuleId()).toBe('rule-1');
  });

  it('reloads list when status filter changes', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();
    discountService.getDiscountRules.mockClear();

    fixture.componentInstance.statusFilter.set('disabled');
    fixture.detectChanges();

    expect(discountService.getDiscountRules).toHaveBeenCalledWith(
      expect.objectContaining({
        status: 'disabled',
      }),
    );
  });

  it('reloads list with backend sort token when sort changes', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();
    discountService.getDiscountRules.mockClear();

    fixture.componentInstance.sortValue.set('name_asc');
    fixture.detectChanges();

    expect(discountService.getDiscountRules).toHaveBeenCalledWith(
      expect.objectContaining({
        sort: 'name_asc',
      }),
    );
  });

  it('loads detail for selected rule id', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    expect(discountService.getDiscountRule).toHaveBeenCalledWith('rule-1');
    expect(fixture.componentInstance.selectedRule()?.id).toBe('rule-1');
  });

  it('disables an active rule and refreshes list', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    discountService.disableDiscountRule.mockReturnValue(of(makeRuleDto({ isActive: false, disabledAt: '2026-02-01T00:00:00Z' })));
    discountService.getDiscountRules.mockClear();

    const component = fixture.componentInstance;
    component.onOpenDisableDialog();
    expect(component.showDisableDialog()).toBe(true);

    component.disableReason.set('No longer needed');
    component.onConfirmDisable();

    expect(discountService.disableDiscountRule).toHaveBeenCalledWith('rule-1', 'No longer needed');
    expect(discountService.getDiscountRules).toHaveBeenCalled();
    expect(component.selectedRule()?.isActive).toBe(false);
  });

  it('shows disable error message when API call fails', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    discountService.disableDiscountRule.mockReturnValue(throwError(() => new Error('fail')));

    const component = fixture.componentInstance;
    component.onOpenDisableDialog();
    component.onConfirmDisable();

    expect(component.detailError()).toBe('discounts.errors.disableFailed');
  });
});
