import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { InventoryService } from '../../inventory/services/inventory.service';
import {
  DiscountRuleDto,
  DiscountRulePreviewDto,
  DiscountService,
} from '../services/discount.service';
import { DiscountRuleEditorDialogComponent } from './discount-rule-editor-dialog.component';

describe('DiscountRuleEditorDialogComponent', () => {
  const makeRule = (overrides: Partial<DiscountRuleDto> = {}): DiscountRuleDto => ({
    id: 'rule-1',
    ruleType: 'BatchPercentage',
    name: '10% off batch',
    description: 'Original rule',
    inventoryBatchId: 'batch-1',
    percentage: 10,
    thresholdAmount: null,
    startsAt: '2026-06-01T00:00:00Z',
    endsAt: '2026-06-30T00:00:00Z',
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

  const makePreview = (overrides: Partial<DiscountRulePreviewDto> = {}): DiscountRulePreviewDto => ({
    affectedCount: 1,
    affectedSample: [
      {
        batchId: 'batch-1',
        itemName: 'Premium Tea',
        batchNumber: 'BN-1',
        salesPrice: 100,
        costPrice: 80,
        discountedPrice: 90,
      },
    ],
    belowCostSample: [],
    safeMaxPercentage: 20,
    errors: [],
    infos: [],
    ...overrides,
  });

  const discountService = {
    previewDiscountRule: vi.fn(),
    createDiscountRule: vi.fn(),
    replaceDiscountRule: vi.fn(),
  };

  const inventoryService = {
    getAvailableBatchesBySearchTerm: vi.fn(),
  };

  beforeEach(() => {
    discountService.previewDiscountRule.mockReset();
    discountService.createDiscountRule.mockReset();
    discountService.replaceDiscountRule.mockReset();
    inventoryService.getAvailableBatchesBySearchTerm.mockReset();

    TestBed.configureTestingModule({
      imports: [
        DiscountRuleEditorDialogComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
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

  it('searches batches, previews, and creates a new batch rule', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;

    inventoryService.getAvailableBatchesBySearchTerm.mockReturnValue(
      of([
        {
          barcode: '111',
          itemName: 'Rice',
          batchNumber: 'BATCH-001',
          inventoryBatchId: 'batch-1',
          quantity: 5,
          salesPrice: 100,
          mrp: 120,
          taxRatePercent: 5,
          taxIncluded: false,
          expiryDate: null,
        },
      ]),
    );
    discountService.previewDiscountRule.mockReturnValue(of(makePreview()));
    discountService.createDiscountRule.mockReturnValue(of(makeRule()));

    component.open('create');
    component.form.controls.ruleType.setValue('BatchPercentage');
    component.form.controls.name.setValue('10% off Rice');
    component.form.controls.percentage.setValue(10);
    component.batchSearchTerm.set('rice');
    component.onBatchSearch();
    component.onSelectBatch({
      barcode: '111',
      itemName: 'Rice',
      batchNumber: 'BATCH-001',
      inventoryBatchId: 'batch-1',
      quantity: 5,
      salesPrice: 100,
      mrp: 120,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
    });

    component.onSubmit();

    expect(inventoryService.getAvailableBatchesBySearchTerm).toHaveBeenCalledWith('rice');
    expect(discountService.previewDiscountRule).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleType: 'BatchPercentage',
        inventoryBatchId: 'batch-1',
        percentage: 10,
      }),
    );
    expect(discountService.createDiscountRule).toHaveBeenCalledWith(
      expect.objectContaining({
        name: '10% off Rice',
        ruleType: 'BatchPercentage',
        inventoryBatchId: 'batch-1',
      }),
    );
  });

  it('requires below-cost confirmation reason before saving', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;

    discountService.previewDiscountRule.mockReturnValue(
      of(
        makePreview({
          belowCostSample: [
            {
              batchId: 'batch-1',
              itemName: 'Premium Tea',
              batchNumber: 'BN-1',
              salesPrice: 100,
              costPrice: 80,
              discountedPrice: 70,
            },
          ],
        }),
      ),
    );

    component.open('create');
    component.form.controls.ruleType.setValue('SalePercentage');
    component.form.controls.name.setValue('Below cost sale');
    component.form.controls.percentage.setValue(30);
    component.form.controls.belowCostConfirmed.setValue(true);
    component.form.controls.belowCostConfirmationReason.setValue('');

    component.onSubmit();

    expect(discountService.createDiscountRule).not.toHaveBeenCalled();
    expect(component.submitErrorKey()).toBe('discounts.editor.errors.belowCostReasonRequired');
    expect(component.submitErrorMessage()).toBe('');
  });

  it('loads an existing rule and submits a replacement version', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;

    discountService.previewDiscountRule.mockReturnValue(of(makePreview()));
    discountService.replaceDiscountRule.mockReturnValue(of(makeRule({ name: 'Updated rule', id: 'rule-2' })));

    component.open('edit', makeRule());
    expect(component.form.controls.name.value).toBe('10% off batch');
    expect(component.form.controls.inventoryBatchId.value).toBe('batch-1');

    component.form.controls.name.setValue('Updated rule');
    component.form.controls.disabledReason.setValue('Replacing old version');

    component.onSubmit();

    expect(discountService.replaceDiscountRule).toHaveBeenCalledWith(
      'rule-1',
      expect.objectContaining({
        name: 'Updated rule',
        disabledReason: 'Replacing old version',
      }),
    );
  });

  it('shows preview errors and does not submit when preview fails', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;

    discountService.previewDiscountRule.mockReturnValue(
      throwError(() => new Error('preview failed')),
    );

    component.open('create');
    component.form.controls.ruleType.setValue('SalePercentage');
    component.form.controls.name.setValue('Sale rule');
    component.form.controls.percentage.setValue(5);

    component.onSubmit();

    expect(component.submitErrorKey()).toBe('discounts.errors.previewFailed');
    expect(component.submitErrorMessage()).toBe('');
    expect(discountService.createDiscountRule).not.toHaveBeenCalled();
    expect(discountService.replaceDiscountRule).not.toHaveBeenCalled();
  });

  it('prefills datetime-local values using local time', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;
    const startsAt = '2026-06-01T10:15:00Z';
    const endsAt = '2026-06-01T11:45:00Z';

    component.open('edit', makeRule({ startsAt, endsAt }));

    const formatLocal = (value: string): string => {
      const date = new Date(value);
      const pad = (part: number): string => String(part).padStart(2, '0');
      return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(
        date.getHours(),
      )}:${pad(date.getMinutes())}`;
    };

    expect(component.form.controls.startsAt.value).toBe(formatLocal(startsAt));
    expect(component.form.controls.endsAt.value).toBe(formatLocal(endsAt));
  });

  it('renders unavailable safe max percentage without a trailing percent sign', () => {
    const fixture = TestBed.createComponent(DiscountRuleEditorDialogComponent);
    const component = fixture.componentInstance;

    discountService.previewDiscountRule.mockReturnValue(
      of(
        makePreview({
          safeMaxPercentage: null,
          affectedSample: [],
          belowCostSample: [],
        }),
      ),
    );

    component.open('create');
    component.form.controls.ruleType.setValue('SalePercentage');
    component.form.controls.name.setValue('Sale rule');
    component.form.controls.percentage.setValue(5);
    component.onPreviewRule();

    fixture.detectChanges();

    const safeMaxValue = fixture.nativeElement.querySelector('.preview-summary .value')?.textContent ?? '';
    expect(safeMaxValue).toContain('discounts.editor.preview.notAvailable');
    expect(safeMaxValue).not.toContain('%');
  });
});
