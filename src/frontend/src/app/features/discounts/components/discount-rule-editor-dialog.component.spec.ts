import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, Subject, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  DiscountRuleDto,
  DiscountRulePreviewDto,
  DiscountService,
} from '../services/discount.service';
import { DiscountConditions } from './discount-rule-editor/discount-conditions-form.component';
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

  const makePreview = (
    overrides: Partial<DiscountRulePreviewDto> = {},
  ): DiscountRulePreviewDto => ({
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

  beforeEach(() => {
    discountService.previewDiscountRule.mockReset();
    discountService.createDiscountRule.mockReset();
    discountService.replaceDiscountRule.mockReset();

    TestBed.configureTestingModule({
      imports: [
        DiscountRuleEditorDialogComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
      providers: [{ provide: DiscountService, useValue: discountService }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  const setBatchConditions = (
    component: DiscountRuleEditorDialogComponent,
    overrides: Partial<DiscountConditions> = {},
  ): void => {
    component.onConditionsChange({
      ruleType: 'BatchPercentage',
      name: component.conditions().name,
      description: component.conditions().description,
      percentage: component.conditions().percentage,
      thresholdAmount: component.conditions().thresholdAmount,
      startsAt: component.conditions().startsAt,
      endsAt: component.conditions().endsAt,
      belowCostConfirmed: component.conditions().belowCostConfirmed,
      belowCostConfirmationReason: component.conditions().belowCostConfirmationReason,
      disabledReason: component.conditions().disabledReason,
      ...overrides,
    });
    component.onTargetSelectionChange(['batch-1']);
  };

  it('creates a new batch rule after a valid preview', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;

    discountService.previewDiscountRule.mockReturnValue(of(makePreview()));
    discountService.createDiscountRule.mockReturnValue(of(makeRule()));

    component.open('create');
    setBatchConditions(component, { name: '10% off Rice', percentage: 10 });
    component.onSubmit();

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
    expect(component.visible()).toBe(false);
  });

  it('does not submit without selected batch', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;
    component.onConditionsChange({
      ...component.conditions(),
      ruleType: 'BatchPercentage',
      name: 'Missing batch rule',
      percentage: 10,
    });

    component.onSubmit();

    expect(discountService.previewDiscountRule).not.toHaveBeenCalled();
    expect(discountService.createDiscountRule).not.toHaveBeenCalled();
    expect(component.submitErrorKey()).toBe('discounts.editor.errors.fixValidation');
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
    setBatchConditions(component, {
      ruleType: 'SalePercentage',
      name: 'Below cost sale',
      percentage: 30,
      belowCostConfirmed: true,
      belowCostConfirmationReason: '',
    });
    component.onTargetSelectionChange([]);
    component.onSubmit();

    expect(discountService.createDiscountRule).not.toHaveBeenCalled();
    expect(component.submitErrorKey()).toBe('discounts.editor.errors.belowCostReasonRequired');
  });

  it('loads an existing rule and submits replacement', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;
    discountService.previewDiscountRule.mockReturnValue(of(makePreview()));
    discountService.replaceDiscountRule.mockReturnValue(
      of(makeRule({ name: 'Updated rule', id: 'rule-2' })),
    );

    component.open('edit', makeRule());
    expect(component.conditions().name).toBe('10% off batch');

    component.onConditionsChange({
      ...component.conditions(),
      name: 'Updated rule',
      disabledReason: 'Replacing old version',
    });
    component.onSubmit();

    expect(discountService.replaceDiscountRule).toHaveBeenCalledWith(
      'rule-1',
      expect.objectContaining({ name: 'Updated rule', disabledReason: 'Replacing old version' }),
    );
  });

  it('shows preview error and does not submit when preview fails', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;
    discountService.previewDiscountRule.mockReturnValue(
      throwError(() => new Error('preview failed')),
    );
    setBatchConditions(component, { ruleType: 'SalePercentage', name: 'Sale rule', percentage: 5 });

    component.onSubmit();

    expect(component.submitErrorKey()).toBe('discounts.errors.previewFailed');
    expect(discountService.createDiscountRule).not.toHaveBeenCalled();
  });

  it('clears stale preview and feedback when conditions change', () => {
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;
    const preview$ = new Subject<DiscountRulePreviewDto>();
    discountService.previewDiscountRule.mockReturnValue(preview$);
    component.open('create');
    setBatchConditions(component, { name: 'First version' });
    component.onPreviewRule();

    component.submitErrorKey.set('discounts.errors.createFailed');
    component.onConditionsChange({ ...component.conditions(), name: 'Changed version' });
    preview$.next(makePreview());

    expect(component.preview()).toBeNull();
    expect(component.submitErrorKey()).toBe('');
  });

  it('ignores conflicting preview and save actions while a request is active', () => {
    const preview$ = new Subject<DiscountRulePreviewDto>();
    const component = TestBed.createComponent(DiscountRuleEditorDialogComponent).componentInstance;
    discountService.previewDiscountRule.mockReturnValue(preview$);
    component.open('create');
    setBatchConditions(component, { name: 'Serialized rule' });

    component.onPreviewRule();
    component.onSubmit();

    expect(discountService.previewDiscountRule).toHaveBeenCalledTimes(1);
    expect(component.visible()).toBe(true);
    component.close();
    expect(component.visible()).toBe(true);
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

    expect(component.conditions().startsAt).toBe(formatLocal(startsAt));
    expect(component.conditions().endsAt).toBe(formatLocal(endsAt));
  });
});
