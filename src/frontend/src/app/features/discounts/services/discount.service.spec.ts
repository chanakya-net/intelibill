import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, it, expect, afterEach } from 'vitest';

import { DISCOUNT_ENDPOINTS } from '../../../core/auth/auth.constants';
import {
  DiscountService,
  DiscountRuleDto,
  DiscountRuleListItemDto,
  DiscountRuleType,
} from './discount.service';

describe('DiscountService', () => {
  function setup(): { service: DiscountService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    return {
      service: TestBed.inject(DiscountService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  const makeListItem = (): DiscountRuleListItemDto => ({
    id: 'rule-1',
    ruleType: 'BatchPercentage',
    name: '10% off batch',
    isActive: true,
    startsAt: null,
    endsAt: null,
    createdAt: '2026-01-01T00:00:00Z',
  });

  const makeRuleDto = (): DiscountRuleDto => ({
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
  });

  it('sends GET to list endpoint with default pagination', () => {
    const { service, http } = setup();
    const response = { items: [makeListItem()], totalCount: 1, pageNumber: 1, pageSize: 20 };

    service.getDiscountRules().subscribe((result) => {
      expect(result.items).toHaveLength(1);
      expect(result.items[0].id).toBe('rule-1');
      expect(result.totalCount).toBe(1);
    });

    const req = http.expectOne(
      (r) =>
        r.url === DISCOUNT_ENDPOINTS.list &&
        r.params.get('page') === '1' &&
        r.params.get('pageSize') === '20',
    );
    expect(req.request.method).toBe('GET');
    req.flush(response);
    http.verify();
  });

  it('sends GET to list endpoint with optional filters', () => {
    const { service, http } = setup();
    const response = { items: [makeListItem()], totalCount: 1, pageNumber: 1, pageSize: 10 };

    service.getDiscountRules({ status: 'active', ruleType: 'BatchPercentage', search: 'batch', page: 2, pageSize: 10 }).subscribe();

    const req = http.expectOne(
      (r) =>
        r.url === DISCOUNT_ENDPOINTS.list &&
        r.params.get('status') === 'active' &&
        r.params.get('ruleType') === 'BatchPercentage' &&
        r.params.get('search') === 'batch' &&
        r.params.get('page') === '2' &&
        r.params.get('pageSize') === '10',
    );
    expect(req.request.method).toBe('GET');
    req.flush(response);
    http.verify();
  });

  it('sends GET to detail endpoint', () => {
    const { service, http } = setup();
    const rule = makeRuleDto();

    service.getDiscountRule('rule-1').subscribe((result) => {
      expect(result.id).toBe('rule-1');
      expect(result.ruleType).toBe('BatchPercentage');
    });

    const req = http.expectOne(DISCOUNT_ENDPOINTS.detail('rule-1'));
    expect(req.request.method).toBe('GET');
    req.flush(rule);
    http.verify();
  });

  it('sends POST to create endpoint with correct payload', () => {
    const { service, http } = setup();
    const rule = makeRuleDto();
    const payload = {
      ruleType: 'BatchPercentage' as DiscountRuleType,
      name: '10% off batch',
      description: null,
      inventoryBatchId: null,
      percentage: 10,
      thresholdAmount: null,
      startsAt: null,
      endsAt: null,
      belowCostConfirmed: false,
      belowCostConfirmationReason: null,
    };

    service.createDiscountRule(payload).subscribe((result) => {
      expect(result.id).toBe('rule-1');
    });

    const req = http.expectOne(DISCOUNT_ENDPOINTS.create);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(rule);
    http.verify();
  });

  it('sends PUT to replace endpoint', () => {
    const { service, http } = setup();
    const rule = makeRuleDto();
    const payload = {
      ruleType: 'SalePercentage' as DiscountRuleType,
      name: 'Updated rule',
      description: null,
      inventoryBatchId: null,
      percentage: 5,
      thresholdAmount: null,
      startsAt: null,
      endsAt: null,
      belowCostConfirmed: false,
      belowCostConfirmationReason: null,
      disabledReason: null,
    };

    service.replaceDiscountRule('rule-1', payload).subscribe((result) => {
      expect(result.id).toBe('rule-1');
    });

    const req = http.expectOne(DISCOUNT_ENDPOINTS.detail('rule-1'));
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush(rule);
    http.verify();
  });

  it('sends POST to disable endpoint', () => {
    const { service, http } = setup();
    const rule = { ...makeRuleDto(), isActive: false };

    service.disableDiscountRule('rule-1', 'No longer needed').subscribe((result) => {
      expect(result.isActive).toBe(false);
    });

    const req = http.expectOne(DISCOUNT_ENDPOINTS.disable('rule-1'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'No longer needed' });
    req.flush(rule);
    http.verify();
  });

  it('sends POST to disable endpoint with null reason', () => {
    const { service, http } = setup();
    const rule = { ...makeRuleDto(), isActive: false };

    service.disableDiscountRule('rule-1', null).subscribe();

    const req = http.expectOne(DISCOUNT_ENDPOINTS.disable('rule-1'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: null });
    req.flush(rule);
    http.verify();
  });
});
