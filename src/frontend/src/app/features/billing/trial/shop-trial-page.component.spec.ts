import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, Subject, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { ShopTrialDrafts } from './shop-trial-drafts';
import { ShopTrialPageComponent } from './shop-trial-page.component';
import { ShopTrialService, type TrialAcceptance, type TrialOperation, type TrialPreview } from './shop-trial.service';

const scopeA = { shopId: 'shop-a', userId: 'owner-a', epoch: 1 };
const scopeB = { shopId: 'shop-b', userId: 'owner-a', epoch: 2 };
const preview: TrialPreview = {
  shopId: scopeA.shopId, eligible: true, reason: null, prospectiveEnd: '2027-02-24T00:00:00.123456Z', activeMembers: 1,
  policy: { policyId: 'p1', policyRevision: 2, termsVersion: 'fixture-v1', memberCap: 5, accountTrialCap: 1, developmentFixture: true },
};

async function render(overrides: Partial<ShopTrialService> = {}) {
  stubMemoryStorage();
  const scope = signal(scopeA);
  const service = {
    preview: vi.fn(() => of({ ...preview, shopId: scope().shopId })),
    accept: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 0 }))),
    recover: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 404 }))),
    ...overrides,
  };
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' }, preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: ShopTrialService, useValue: service },
      { provide: ShopContextService, useValue: { scope, epoch: () => scope().epoch,
        isCurrent: (value: typeof scopeA) => value.shopId === scope().shopId && value.userId === scope().userId } },
    ],
  });
  const fixture = TestBed.createComponent(ShopTrialPageComponent);
  const page = fixture.nativeElement as HTMLElement;
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, scope, service, page, drafts: TestBed.inject(ShopTrialDrafts) };
}

function confirm(page: HTMLElement) {
  const checkbox = page.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
  checkbox.checked = true;
  checkbox.dispatchEvent(new Event('change'));
}

function startButton(page: HTMLElement) {
  return [...page.querySelectorAll('button')].find(button => button.textContent?.includes('Start 14-day trial'))!;
}

afterEach(() => {
  TestBed.resetTestingModule();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('shop trial confirmation', () => {
  it('requires explicit acceptance and preserves unknown outcomes', async () => {
    const { fixture, service, page, drafts } = await render();
    expect(startButton(page).disabled).toBe(true);
    expect(service.accept).not.toHaveBeenCalled();
    expect(page.textContent).toContain('05:30:00.123456 IST');
    confirm(page);
    fixture.detectChanges();
    startButton(page).click();
    await fixture.whenStable();
    fixture.detectChanges();
    const acceptance = drafts.read(scopeA);
    expect(acceptance).toMatchObject({ termsAccepted: true, policyId: 'p1', policyRevision: 2, termsVersion: 'fixture-v1' });
    expect(service.accept).toHaveBeenCalledExactlyOnceWith(scopeA, acceptance);
    expect(page.getElementsByTagName('button')[0].textContent).toContain('Retry the same activation');

    fixture.destroy();
    const reloaded = TestBed.createComponent(ShopTrialPageComponent);
    reloaded.detectChanges();
    await reloaded.whenStable();
    expect(service.recover).toHaveBeenCalledExactlyOnceWith(scopeA, acceptance!.operationKey);
    expect(drafts.read(scopeA)).toEqual(acceptance);
    expect(service.accept).toHaveBeenCalledTimes(1);
  });

  it('keeps the original operation when the active shop changes before a reply arrives', async () => {
    const response = new Subject<TrialOperation>();
    const accept = vi.fn(() => response.asObservable());
    const { fixture, scope, page, drafts } = await render({ accept });
    confirm(page);
    fixture.detectChanges();
    startButton(page).click();
    const acceptance = drafts.read(scopeA) as TrialAcceptance;
    scope.set(scopeB);
    fixture.detectChanges();
    response.next({
      operationKey: acceptance.operationKey, status: 'Applied', reason: null,
      acceptedAt: '2027-02-10T00:00:00Z', endsAt: preview.prospectiveEnd,
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.textContent).not.toContain('Trial activated');
    expect(drafts.read(scopeA)).toEqual(acceptance);
    expect(drafts.read(scopeB)).toBeNull();
  });

  it('never posts without durable browser storage', async () => {
    const { fixture, service, page } = await render();
    vi.spyOn(localStorage, 'setItem').mockImplementation(() => { throw new Error('fixture storage failure'); });
    confirm(page);
    fixture.detectChanges();
    startButton(page).click();
    fixture.detectChanges();
    expect(page.textContent).toContain('Trial storage is unavailable');
    expect(service.accept).not.toHaveBeenCalled();
  });
});
