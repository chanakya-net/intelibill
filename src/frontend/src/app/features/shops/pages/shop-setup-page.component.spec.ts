import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { SHOP_ENDPOINTS } from '../../../core/auth/auth.constants';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { AppReloadService } from '../../../core/services/app-reload.service';
import type { ShopSetupStatus } from '../services/shop-setup.models';
import { ShopSetupPageComponent } from './shop-setup-page.component';

const scope = { shopId: 'shop', userId: 'owner' };
const en = JSON.parse(readFileSync('public/assets/i18n/en-IN.json', 'utf8'));
const status: ShopSetupStatus = {
  shopId: scope.shopId,
  contractVersion: 1,
  canTrade: true,
  steps: [
    { id: 'catalog', state: 'complete', reasons: [] },
    { id: 'stock', state: 'notApplicable', reasons: [] },
    { id: 'sample', state: 'optional', reasons: [] },
    { id: 'bank', state: 'optional', reasons: [] },
  ],
  blockingReasons: [],
  permissions: { canEditShop: true, canEditCatalog: true, canSavePreferences: true },
  preferences: { version: 0, showOnStartup: true, sampleReceiptAcknowledgedAt: null },
};

describe(ShopSetupPageComponent.name, () => {
  let http: HttpTestingController;
  const url = SHOP_ENDPOINTS.setup(scope.shopId);

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ShopSetupPageComponent, TranslocoTestingModule.forRoot({
        langs: { 'en-IN': en },
        translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
        preloadLangs: true,
      })],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AppReloadService, useValue: { restartAtAppRoot: vi.fn() } },
        { provide: AuthService, useValue: { session: signal(null) } },
      ],
    });
    TestBed.inject(ShopContextService).initialize(scope);
    http = TestBed.inject(HttpTestingController);
  });

  it('shows readiness from saved facts after remount, with optional bank and no service stock action', () => {
    for (let reload = 0; reload < 2; reload++) {
      const view = TestBed.createComponent(ShopSetupPageComponent);
      http.expectOne(url).flush(status);
      view.detectChanges();
      const element = view.nativeElement as HTMLElement;
      expect(element.textContent).toContain('Ready to start billing');
      expect(element.querySelector('[data-step="stock"]')?.textContent).toContain('Not applicable');
      expect(element.querySelector('[data-step="stock"] a')).toBeNull();
      expect(element.querySelector('[data-step="bank"]')?.textContent).toContain('Optional');
      view.destroy();
    }
    http.verify();
  });

  it('refreshes a stale save without claiming it saved or leaving setup', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const view = TestBed.createComponent(ShopSetupPageComponent);
    http.expectOne(url).flush(status);
    view.componentInstance.leave('/dashboard');

    const put = http.expectOne(`${url}/preferences`);
    expect(put.request.body).toEqual({ expectedVersion: 0, showOnStartup: false, acknowledgeSample: false });
    put.flush({}, { status: 409, statusText: 'Conflict' });
    http.expectOne(url).flush({ ...status, preferences: { ...status.preferences, version: 1 } });
    view.detectChanges();
    expect((view.nativeElement as HTMLElement).textContent).toContain('Setup preferences changed');
    expect((view.nativeElement as HTMLElement).textContent).toContain('Leave setup without saving');
    expect(navigate).not.toHaveBeenCalled();
    http.verify();
  });

  it('renders a fallback for unknown reasons and hides actions without permission', () => {
    const view = TestBed.createComponent(ShopSetupPageComponent);
    http.expectOne(url).flush({
      ...status,
      canTrade: false,
      steps: [{ id: 'tax', state: 'incomplete', reasons: ['Unknown.FutureReason'] }],
      permissions: { canEditShop: false, canEditCatalog: false, canSavePreferences: false },
    });
    view.detectChanges();
    const element = view.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Review the saved product, stock and tax details, then refresh.');
    expect(element.textContent).toContain('A shop owner can change this setting.');
    expect(element.textContent).not.toContain('Edit tax and numbering');
    http.verify();
  });
});
