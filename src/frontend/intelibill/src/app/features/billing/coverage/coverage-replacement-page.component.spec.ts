import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { stubMemoryStorage } from '../billing.test-utils';
import { CoverageReplacementPageComponent } from './coverage-replacement-page.component';

const accountId = '33333333-3333-4333-8333-333333333333';
const termId = '44444444-4444-4444-8444-444444444444';
const northId = '11111111-1111-4111-8111-111111111111';
const southId = '22222222-2222-4222-8222-222222222222';

function mount(params: Record<string, string>) {
  const scope = signal({ userId: 'u1', shopId: 's1', epoch: 1 });
  const paramMap = new BehaviorSubject(convertToParamMap(params));
  TestBed.configureTestingModule({
    imports: [
      CoverageReplacementPageComponent,
      TranslocoTestingModule.forRoot({ langs: { 'en-IN': en }, translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' } }),
    ],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ShopContextService, useValue: { scope } },
      { provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } },
      { provide: ActivatedRoute, useValue: { paramMap: paramMap.asObservable(), snapshot: { paramMap: paramMap.value } } },
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(CoverageReplacementPageComponent);
  fixture.detectChanges();
  return { fixture, http };
}

async function flush(fixture: { detectChanges(): void }) {
  await Promise.resolve();
  await Promise.resolve();
  fixture.detectChanges();
}

describe('CoverageReplacementPageComponent cross-tab recovery, deadline reset and truthful status', () => {
  beforeEach(() => {
    stubMemoryStorage();
    vi.stubGlobal('navigator', { locks: { request: (_: string, run: () => unknown) => Promise.resolve(run()) } });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });


  it('retains the original renewal after a timeout and reload, including an empty recovery response', async () => {
    const { fixture, http } = mount({ accountId, termId });
    const preview = { termId, canSchedule: true, capacity: 3, memberAllowance: 5, effectiveAt: '2028-02-29T04:30:00Z',
      coveredShops: [{ shopId: northId, shopName: 'North', permitDeadline: null }],
      eligibleShops: [{ shopId: southId, shopName: 'South' }], replacements: [], hasMore: false };
    http.expectOne(r => r.method === 'GET').flush(preview);
    await flush(fixture);
    const el: HTMLElement = fixture.nativeElement;
    for (const [label, id] of [['outgoing', northId], ['incoming', southId]]) {
      const choice = el.querySelector<HTMLSelectElement>(`[data-testid="replacement-${label}"]`)!;
      choice.value = id;
      choice.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    }
    for (const box of el.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) {
      box.checked = true;
      box.dispatchEvent(new Event('change'));
    }
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-testid="replacement-confirm"]')!.click();
    await flush(fixture);
    const pending = http.expectOne(r => r.method === 'POST');
    const original = pending.request.body;
    pending.flush(null, { status: 503, statusText: 'Unavailable' });
    await flush(fixture);
    fixture.destroy();
    const restored = TestBed.createComponent(CoverageReplacementPageComponent);
    restored.detectChanges();
    http.expectOne(r => r.method === 'GET').flush(preview);
    await flush(restored);
    expect(restored.nativeElement.textContent).toContain('North → South');
    expect(restored.nativeElement.querySelector('[data-testid="replacement-confirm"]')).toBeNull();
    const retry = restored.nativeElement.querySelector('[data-testid="replacement-retry"]') as HTMLButtonElement;
    retry.click();
    await flush(restored);
    const replay = http.expectOne(r => r.method === 'POST');
    expect(replay.request.body).toEqual(original);
    replay.flush(null, { status: 404, statusText: 'Missing' });
    await flush(restored);
    expect(restored.nativeElement.textContent).toContain('North → South');
    http.verify();
  });
});
