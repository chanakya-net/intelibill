import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, Subject, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import en from '../../../../../../public/assets/i18n/en-IN.json';
import { ShopTillSettingsComponent } from './shop-till-settings.component';
import {
  ShopTillSettingsService,
  type ShopTillSettings,
} from '../../services/shop-till-settings.service';

const initial: ShopTillSettings = {
  shopId: 'shop-a',
  available: true,
  enabled: false,
  version: 0,
  openSessionCount: 0,
  awaitingReviewCount: 0,
  pendingOfflineCount: 0,
};
describe('ShopTillSettingsComponent', () => {
  const scope = signal('owner:shop-a:1');
  const activeShop = signal('shop-a');
  const role = signal('owner');
  const api = {
    scopeIdentity: () => scope(),
    activeShopId: () => activeShop(),
    role: () => role(),
    read: vi.fn(),
    update: vi.fn(),
  };
  function setup(state = initial, canEdit = true) {
    api.read.mockReturnValue(of(state));
    TestBed.configureTestingModule({
      imports: [
        ShopTillSettingsComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': en },
          translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
          preloadLangs: true,
        }),
      ],
      providers: [provideRouter([]), { provide: ShopTillSettingsService, useValue: api }],
    });
    const fixture = TestBed.createComponent(ShopTillSettingsComponent);
    fixture.componentRef.setInput('shopId', 'shop-a');
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.detectChanges();
    return {
      fixture,
      component: fixture.componentInstance,
      root: fixture.nativeElement as HTMLElement,
    };
  }
  beforeEach(() => {
    vi.clearAllMocks();
    scope.set('owner:shop-a:1');
    activeShop.set('shop-a');
    role.set('owner');
  });
  afterEach(() => TestBed.resetTestingModule());
  it('requires a reason and acknowledgement and waits for the saved response before enabling', () => {
    const { component, fixture } = setup();
    const saved = new Subject<ShopTillSettings>();
    api.update.mockReturnValue(saved);
    component.beginChange();
    component.save();
    expect(api.update).not.toHaveBeenCalled();
    component.reason = 'Start cashier counts';
    component.confirmed = true;
    component.save();
    expect(api.update).toHaveBeenCalledWith('shop-a', {
      enabled: true,
      expectedVersion: 0,
      reason: 'Start cashier counts',
      confirmed: true,
    });
    expect(component.state()?.enabled).toBe(false);
    saved.next({ ...initial, enabled: true, version: 1 });
    saved.complete();
    fixture.detectChanges();
    expect(component.state()?.enabled).toBe(true);
    expect(component.editing()).toBe(false);
  });
  it('keeps rollout-unavailable and owner-only states read-only', () => {
    const { component, root } = setup({ ...initial, available: false });
    component.beginChange();
    expect(component.editing()).toBe(false);
    expect(root.textContent).toContain('not available');
    expect(root.querySelector('[data-testid="till-settings-change"]')).toBeNull();
  });
  it('refuses manager edits even if an obsolete input still says owner', () => {
    const { component, fixture } = setup();
    role.set('manager');
    fixture.detectChanges();
    component.beginChange();
    component.reason = 'Try';
    component.confirmed = true;
    component.save();
    expect(api.update).not.toHaveBeenCalled();
    expect(component.editing()).toBe(false);
  });
  it.each(['openSessionCount', 'awaitingReviewCount', 'pendingOfflineCount'] as const)(
    'blocks disable while %s remains unresolved',
    (field) => {
      const { component } = setup({ ...initial, enabled: true, version: 2, [field]: 1 });
      component.beginChange();
      expect(component.editing()).toBe(false);
      expect(component.blocked()).toBe(true);
    },
  );
  it('does not fetch or change a selected shop outside the active scope', () => {
    activeShop.set('shop-b');
    const { component, root } = setup();
    expect(api.read).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Switch to this shop');
    component.beginChange();
    expect(component.editing()).toBe(false);
  });
  it('ignores a late response after leaving and returning to the same shop', () => {
    const { component, fixture } = setup();
    const saved = new Subject<ShopTillSettings>();
    api.update.mockReturnValue(saved);
    component.beginChange();
    component.reason = 'Counts';
    component.confirmed = true;
    component.save();
    scope.set('owner:shop-a:3');
    fixture.detectChanges();
    saved.next({ ...initial, enabled: true, version: 1 });
    expect(component.state()?.enabled).toBe(false);
  });
  it('retains an unknown update when an early GET still returns the old version', () => {
    const { component, fixture } = setup();
    api.update.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    component.beginChange();
    component.reason = 'Counts';
    component.confirmed = true;
    component.save();
    fixture.detectChanges();
    expect(component.uncertain()).toBe(true);
    component.refresh();
    expect(component.uncertain()).toBe(true);
    component.beginChange();
    expect(api.update).toHaveBeenCalledTimes(1);
    api.read.mockReturnValue(of({ ...initial, enabled: true, version: 1 }));
    component.refresh();
    expect(component.uncertain()).toBe(false);
    expect(component.state()?.enabled).toBe(true);
  });
  it('shows a stale-version error and requires refreshed settings', () => {
    const { component } = setup();
    api.update.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 409, error: { code: 'Till.SettingsVersionConflict' } }),
      ),
    );
    component.beginChange();
    component.reason = 'Counts';
    component.confirmed = true;
    component.save();
    expect(component.error()).toBe('tills.settings.changed');
    expect(component.editing()).toBe(false);
  });
});
