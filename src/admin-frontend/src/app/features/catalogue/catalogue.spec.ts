import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AdminSessionService, type SessionNotice } from '../auth/admin-session.service';
import { CatalogueApi, type PlanItem, type TrialDraft } from './catalogue-api.service';
import { formatInr, invalid, paiseToRupees, parseCount, rupeesToPaise } from './catalogue-money';
import type { PublishedVersion } from './catalogue-publication.models';
import { CataloguePageComponent } from './catalogue-page.component';

// Browser-only fixtures: every API answer below is mocked. The real licensing API is exercised by P0206BrowserTests.
const incomplete: PlanItem = {
  planId: 'plan-1', kind: 'multi-shop',
  draft: {
    versionId: 'version-1', versionNumber: 1, status: 'draft', version: 4, currency: 'INR', name: 'Growth',
    monthlyPricePaise: 149900, annualPricePaise: null, includedShops: null, membersPerShop: null,
    extraShopsOffered: null, extraShopMonthlyPricePaise: null, extraShopAnnualPricePaise: null,
    customerTermsVersion: null, refundPolicyVersion: null, negotiationReason: null,
    missing: ['annualPricePaise', 'includedShops'], complete: false, updatedAt: '2026-11-16T09:00:00Z',
  },
};

const trialDraft: TrialDraft = {
  versionId: 'trial-1', versionNumber: 1, status: 'draft', version: 2, durationDays: 14, activeMemberCap: 5,
  maxConcurrentTrialShops: null, trialTermsVersion: null, missing: ['maxConcurrentTrialShops', 'trialTermsVersion'],
  complete: false, updatedAt: '2026-11-16T09:00:00Z',
};

function render(capabilities: string[] = ['licensing-manager'], trial: TrialDraft | null = null,
  history: PublishedVersion[] = []) {
  const grants = signal(capabilities);
  const notice = signal<SessionNotice>('none');
  const api = {
    listPlans: vi.fn(() => of({ items: [incomplete], nextCursor: null })),
    history: vi.fn(() => of({ items: history, nextCursor: null, currentTrialVersionId: null })),
    retirePlan: vi.fn((_target: unknown, _body: { operationKey: string }) =>
      throwError(() => new HttpErrorResponse({ status: 0 }))),
    recoverRetirement: vi.fn((_key: string) => throwError(() => new HttpErrorResponse({ status: 404 }))),
    trialDraft: vi.fn(() => of({ draft: trial })),
    savePlan: vi.fn((_id: string, command: { name: string }) =>
      of({ ...incomplete, draft: { ...incomplete.draft, name: command.name, version: 5 } })),
    saveTrial: vi.fn(() => of({ draft: trialDraft })),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AdminSessionService, useValue: { capabilities: grants, notice, sessionId: signal('test-session'), check: vi.fn(() => of('active')) } },
      { provide: CatalogueApi, useValue: api },
    ],
  });
  const view = TestBed.createComponent(CataloguePageComponent);
  view.detectChanges();
  const page = view.nativeElement as HTMLElement;
  const field = (selector: string, value: string) => {
    const element = page.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(selector)!;
    element.value = value;
    element.dispatchEvent(new Event('change'));
    view.detectChanges();
  };
  const submit = (selector: string) => {
    page.querySelector(selector)!.dispatchEvent(new Event('submit', { cancelable: true }));
    view.detectChanges();
  };
  const click = (text: string) => {
    [...page.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent!.includes(text))!.click();
    view.detectChanges();
  };
  return { page, view, api, field, submit, click };
}

describe('catalogue money and capacity parsing', () => {
  it('converts rupee text to whole paise without floating point', () => {
    expect(rupeesToPaise('1,499.00')).toBe(149900);
    expect(rupeesToPaise('0.07')).toBe(7);
    expect(rupeesToPaise('0')).toBe(0);
    expect(rupeesToPaise('  ')).toBeNull();
    expect(rupeesToPaise('-1')).toBe(invalid);
    expect(rupeesToPaise('1.005')).toBe(invalid);
    expect(rupeesToPaise('10000000000')).toBe(1_000_000_000_000);
    expect(rupeesToPaise('10000000000.01')).toBe(invalid);
    expect(paiseToRupees(149907)).toBe('1499.07');
    expect(formatInr(149900)).toBe('₹1,499.00');
    expect(formatInr(null)).toBe('Not set');
  });

  it('accepts only explicit capacities, never zero or unlimited', () => {
    expect(parseCount('5')).toBe(5);
    expect(parseCount('')).toBeNull();
    for (const text of ['0', '-1', 'unlimited', '100001', '2.5']) expect(parseCount(text)).toBe(invalid);
  });
});

describe('catalogue draft editor', () => {
  it('makes no catalogue call without the Licensing Manager grant', () => {
    const { page, api } = render(['support', 'platform-owner']);
    expect(api.listPlans).not.toHaveBeenCalled();
    expect(api.trialDraft).not.toHaveBeenCalled();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Only a Licensing Manager');
  });

  it('shows an incomplete draft as inactive with its missing facts', () => {
    const { page, click } = render();
    expect(page.textContent).toContain('Draft · not published · incomplete');
    click('Growth');
    expect(page.querySelector('[data-testid="draft-status"]')?.textContent).toContain('Draft · not published');
    expect(page.querySelector('[data-testid="missing-facts"]')?.textContent).toContain('Annual base price');
    expect(page.querySelector<HTMLInputElement>('#monthly')!.value).toBe('1499.00');
    expect(page.querySelector<HTMLInputElement>('#annual')!.value).toBe('');
  });

  it('sends blank as missing, zero as a fact and the loaded draft version and revision', () => {
    const { page, api, click, field, submit } = render();
    click('Growth');
    field('#monthly', '0');
    field('#members', '');
    submit('adm-plan-draft-editor form');
    expect(api.savePlan).toHaveBeenCalledWith('plan-1', expect.objectContaining({
      expectedVersion: 4, expectedDraftVersionId: 'version-1', kind: 'multi-shop', name: 'Growth',
      monthlyPricePaise: 0, annualPricePaise: null, membersPerShop: null, extraShopsOffered: null, reason: null,
    }));
    expect(page.querySelector('[role="status"]')?.textContent).toContain('not published');
  });

  it.each(['', 'no'])('refuses an extra-shop price when the offer is "%s"', (offer) => {
    const { page, api, click, field, submit } = render();
    click('Growth');
    field('#offer', offer);
    field('#extra-monthly', '0');
    submit('adm-plan-draft-editor form');
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Extra-shop prices need an extra-shop offer');
    expect(api.savePlan).not.toHaveBeenCalled();

    field('#offer', 'yes');
    submit('adm-plan-draft-editor form');
    expect(api.savePlan).toHaveBeenCalledWith('plan-1', expect.objectContaining({
      extraShopsOffered: true, extraShopMonthlyPricePaise: 0, extraShopAnnualPricePaise: null,
    }));
  });

  it('blocks negotiated Custom terms without a reason and creates with a fixed new identity', () => {
    const { page, api, click, field, submit } = render();
    click('New plan draft');
    field('#plan-kind', 'custom');
    field('#plan-name', 'Chain');
    field('#annual', '50,000');
    submit('adm-plan-draft-editor form');
    expect(api.savePlan).not.toHaveBeenCalled();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('need a reason');
    field('#reason', 'Twelve-shop chain');
    api.savePlan.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    submit('adm-plan-draft-editor form');
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('not saved');
    submit('adm-plan-draft-editor form');
    const [first, second] = api.savePlan.mock.calls;
    expect(first[0]).toBe(second[0]);
    expect(first[1]).toEqual(expect.objectContaining({
      expectedVersion: 0, kind: 'custom', annualPricePaise: 5_000_000, reason: 'Twelve-shop chain',
      extraShopsOffered: null,
    }));
    expect(first[1]).not.toHaveProperty('expectedDraftVersionId');
  });

  it('clears the editor after a version conflict until the catalogue reloads', () => {
    const { page, api, click, submit } = render();
    api.savePlan.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 409, error: { code: 'Catalogue.DraftVersionConflict' },
    })));
    click('Growth');
    submit('adm-plan-draft-editor form');
    expect(page.querySelector('#plan-name')).toBeNull();
    expect(page.textContent).toContain('Reload before editing');
    click('Reload catalogue');
    expect(api.listPlans).toHaveBeenCalledTimes(2);
  });

  it('keeps trial caps explicit with a fixed fourteen-day trial', () => {
    const { page, api, field, submit } = render(['licensing-manager'], trialDraft);
    expect(page.textContent).toContain('fixed 14 calendar days');
    expect(page.querySelector('[data-testid="trial-missing"]')?.textContent).toContain('Concurrent trial shop cap');
    field('#shop-cap', '0');
    submit('adm-trial-policy-editor form');
    expect(api.saveTrial).not.toHaveBeenCalled();
    field('#shop-cap', '2');
    field('#trial-terms', 'trial-2026.11');
    submit('adm-trial-policy-editor form');
    expect(api.saveTrial).toHaveBeenCalledWith({
      expectedVersion: 2, expectedDraftVersionId: 'trial-1', activeMemberCap: 5, maxConcurrentTrialShops: 2,
      trialTermsVersion: 'trial-2026.11',
    });
  });

  it('creates the first trial policy draft without naming a draft version', () => {
    const { api, field, submit } = render();
    field('#member-cap', '5');
    submit('adm-trial-policy-editor form');
    expect(api.saveTrial).toHaveBeenCalledWith({
      expectedVersion: 0, activeMemberCap: 5, maxConcurrentTrialShops: null, trialTermsVersion: null,
    });
  });
});

it('shows immutable publication history on the Licensing Manager catalogue', () => {
  const { page } = render();
  expect([...page.querySelectorAll('h2')].map(heading => heading.textContent?.trim())).toContain('Published versions');
});


const publishedPlan: PublishedVersion = {
  planId: 'plan-1', versionId: 'version-1', version: 2, status: 'published', publishedAt: '2026-11-16T09:00:00Z',
  snapshot: {
    plan: { ...incomplete.draft, kind: 'multi-shop', developmentFixture: true,
      trialPolicy: { ...trialDraft, developmentFixture: true }, eligiblePaidGraceDays: 7, graceExclusions: [],
      billingTimeZone: 'Asia/Kolkata' },
    negotiationReason: null, accountingTreatmentReference: 'fixture-accounting',
  },
};

it('retires after confirmation and keeps the original key after an unknown outcome and missing lookup', () => {
  const { page, view, api, click, field, submit } = render(undefined, null, [publishedPlan]);
  click('Retire from new purchase');
  const confirm = page.querySelector<HTMLButtonElement>('adm-catalogue-retirement button[type="submit"]')!;
  expect(confirm.disabled).toBe(true);
  field('#retirement-reason', 'Local fixture withdrawal');
  const checkbox = page.querySelector<HTMLInputElement>('adm-catalogue-retirement input[type="checkbox"]')!;
  checkbox.checked = true;
  checkbox.dispatchEvent(new Event('change'));
  view.detectChanges();
  expect(confirm.disabled).toBe(false);
  submit('adm-catalogue-retirement form');
  const key = api.retirePlan.mock.calls[0]![1].operationKey;
  expect(TestBed.inject(Location).path()).toContain('retirement=' + key);
  expect(page.querySelector('[role="alert"]')?.textContent).toContain('retirement outcome is unconfirmed');

  click('Check retirement outcome');
  expect(api.recoverRetirement).toHaveBeenCalledWith(key);
  expect(TestBed.inject(Location).path()).toContain('retirement=' + key);
  click('Retry original retirement');
  expect(api.retirePlan.mock.calls[1]).toEqual(api.retirePlan.mock.calls[0]);
});
