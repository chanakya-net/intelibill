import { of, Subject, throwError } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { InventoryService } from '../../inventory/services/inventory.service';
import {
  DiscountRuleDto,
  DiscountRuleListItemDto,
  DiscountService,
} from '../services/discount.service';
import { DiscountsPageComponent } from './discounts-page.component';

describe('DiscountsPageComponent', () => {
  const translations = {
    shell: {
      manageDiscounts: 'Manage discounts',
    },
    discounts: {
      title: 'Discount Rules',
      subtitle: 'Review, filter, and disable existing discount rules.',
      showingCount: 'Showing {{visible}} of {{total}} rules',
      summary: {
        totalRules: 'Total rules',
        activeOnPage: 'Active on page',
        disabledOnPage: 'Disabled on page',
        expiredOnPage: 'Expired on page',
        onThisPage: 'On this page',
      },
      actions: {
        disable: 'Disable',
      },
      list: {
        title: 'Rules',
      },
      detail: {
        title: 'Rule details',
      },
      fields: {
        name: 'Name',
        ruleType: 'Rule type',
        isActive: 'Status',
        startsAt: 'Starts at',
        endsAt: 'Ends at',
      },
      filters: {
        reset: 'Reset filters',
        status: {
          label: 'Status',
          active: 'Active',
          disabled: 'Disabled',
          expired: 'Expired',
          all: 'All',
        },
        type: {
          label: 'Rule type',
          all: 'All types',
        },
        sort: {
          label: 'Sort',
          createdDesc: 'Created newest',
          createdAsc: 'Created oldest',
          nameAsc: 'Name A-Z',
          nameDesc: 'Name Z-A',
        },
        search: {
          label: 'Search',
          placeholder: 'Search rules',
        },
      },
      pagination: {
        prev: 'Previous page',
        next: 'Next page',
        pageInfo: 'Page {{page}} of {{total}}',
      },
      ruleType: {
        BatchPercentage: 'Batch discount',
        SalePercentage: 'Sale discount',
        SaleThresholdPercentage: 'Threshold discount',
      },
      status: {
        active: 'Active',
        disabled: 'Disabled',
        expired: 'Expired',
      },
      editor: {
        actions: {
          createRule: 'Create rule',
          editRule: 'Edit rule',
        },
      },
    },
  };
  const alternateTranslations = {
    ...translations,
    discounts: {
      ...translations.discounts,
      filters: {
        ...translations.discounts.filters,
        status: {
          ...translations.discounts.filters.status,
          active: 'Active translated',
        },
        type: {
          ...translations.discounts.filters.type,
          all: 'All types translated',
        },
        sort: {
          ...translations.discounts.filters.sort,
          createdDesc: 'Created newest translated',
        },
      },
    },
  };

  const makeListItem = (
    overrides: Partial<DiscountRuleListItemDto> = {},
  ): DiscountRuleListItemDto => ({
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
      imports: [
        DiscountsPageComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: translations, alt: alternateTranslations },
          translocoConfig: { availableLangs: ['en', 'alt'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
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

  it('renders translated selected labels for closed filters', async () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const selectedLabels = Array.from(
      fixture.nativeElement.querySelectorAll('app-discounts-filter-bar .p-select-label'),
      (element) => (element as HTMLElement).textContent?.trim() ?? '',
    );

    expect(selectedLabels).toEqual(
      expect.arrayContaining(['Active', 'All types', 'Created newest']),
    );
    expect(selectedLabels.some((label) => label.includes('discounts.filters.'))).toBe(false);
  });

  it('updates closed filter labels when language changes', async () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    const transloco = TestBed.inject(TranslocoService);
    fixture.detectChanges();
    await fixture.whenStable();

    transloco.setActiveLang('alt');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const selectedLabels = Array.from(
      fixture.nativeElement.querySelectorAll('app-discounts-filter-bar .p-select-label'),
      (element) => (element as HTMLElement).textContent?.trim() ?? '',
    );

    expect(selectedLabels).toEqual(
      expect.arrayContaining([
        'Active translated',
        'All types translated',
        'Created newest translated',
      ]),
    );
    expect(selectedLabels.some((label) => label.includes('discounts.filters.'))).toBe(false);
  });

  it('renders list and detail header actions', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    const createButton = fixture.nativeElement.querySelector(
      '[data-testid="discounts-create-rule"]',
    );
    const editButton = fixture.nativeElement.querySelector('[data-testid="discounts-edit-rule"]');
    const disableButton = fixture.nativeElement.querySelector('[data-testid="discounts-disable"]');

    expect(createButton).not.toBeNull();
    expect(editButton).not.toBeNull();
    expect(disableButton).not.toBeNull();
    expect((createButton as HTMLElement).textContent).toContain('Create rule');
    expect((editButton as HTMLElement).textContent).toContain('Edit rule');
    expect((disableButton as HTMLElement).textContent).toContain('Disable');
  });

  it('loads detail for selected rule id', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    expect(discountService.getDiscountRule).toHaveBeenCalledWith('rule-1');
    expect(fixture.componentInstance.selectedRule()?.id).toBe('rule-1');
  });

  it('reconciles selection when the returned page no longer contains the selected rule', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    discountService.getDiscountRules.mockReturnValue(
      of({
        items: [makeListItem({ id: 'rule-2', name: 'Replacement row' })],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 20,
      }),
    );
    discountService.getDiscountRule.mockReturnValue(of(makeRuleDto({ id: 'rule-2' })));

    fixture.componentInstance.onSearchChange('replacement');
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedRuleId()).toBe('rule-2');
    expect(fixture.componentInstance.selectedRule()?.id).toBe('rule-2');
  });

  it('clears stale detail when the returned page is empty', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    discountService.getDiscountRules.mockReturnValue(
      of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20 }),
    );

    fixture.componentInstance.onSearchChange('missing');
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedRuleId()).toBeNull();
    expect(fixture.componentInstance.selectedRule()).toBeNull();
  });

  it('ignores a stale list response after a newer filter request completes', () => {
    const stale = new Subject<{
      items: DiscountRuleListItemDto[];
      totalCount: number;
      pageNumber: number;
      pageSize: number;
    }>();
    discountService.getDiscountRules.mockImplementation((params: { search?: string }) => {
      if (params.search === 'stale') return stale;
      if (params.search === 'current') {
        return of({
          items: [makeListItem({ id: 'rule-current' })],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 20,
        });
      }
      return of({ items: [makeListItem()], totalCount: 1, pageNumber: 1, pageSize: 20 });
    });
    discountService.getDiscountRule.mockImplementation((id: string) =>
      of(makeRuleDto({ id })),
    );
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.onSearchChange('stale');
    fixture.detectChanges();
    fixture.componentInstance.onSearchChange('current');
    fixture.detectChanges();
    stale.next({
      items: [makeListItem({ id: 'rule-stale' })],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
    });
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedRuleId()).toBe('rule-current');
  });

  it('disables an active rule and refreshes list', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    discountService.disableDiscountRule.mockReturnValue(
      of(makeRuleDto({ isActive: false, disabledAt: '2026-02-01T00:00:00Z' })),
    );
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

  it('shows disable error inside the still-open confirmation dialog when API call fails', () => {
    const fixture = TestBed.createComponent(DiscountsPageComponent);
    fixture.detectChanges();

    discountService.disableDiscountRule.mockReturnValue(throwError(() => new Error('fail')));

    const component = fixture.componentInstance;
    component.onOpenDisableDialog();
    component.onConfirmDisable();

    expect(component.disableError()).toBe('discounts.errors.disableFailed');
    expect(component.detailError()).toBe('');
    expect(component.showDisableDialog()).toBe(true);
  });
});
