import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { SetDefaultStoreOverlayComponent } from './set-default-store-overlay.component';
import { ShopService } from '../../shops/services/shop.service';

describe('SetDefaultStoreOverlayComponent', () => {
  const shopService = {
    setDefaultShop: vi.fn<ShopService['setDefaultShop']>(),
  };

  function setup(): SetDefaultStoreOverlayComponent {
    TestBed.configureTestingModule({
      imports: [SetDefaultStoreOverlayComponent],
      providers: [{ provide: ShopService, useValue: shopService }],
    });

    const fixture = TestBed.createComponent(SetDefaultStoreOverlayComponent);
    fixture.componentRef.setInput('activeShopId', 'shop-1');
    fixture.componentRef.setInput('shops', [
      { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
      { shopId: 'shop-2', shopName: 'Branch', role: 'Manager', isDefault: false, lastUsedAt: null },
    ]);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    shopService.setDefaultShop.mockReset();
    shopService.setDefaultShop.mockReturnValue(of(void 0));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('closes directly when selecting currently active shop', () => {
    const component = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.onSetDefault('shop-1');

    expect(shopService.setDefaultShop).not.toHaveBeenCalled();
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('calls setDefaultShop and emits close on success', () => {
    const component = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.onSetDefault('shop-2');

    expect(shopService.setDefaultShop).toHaveBeenCalledWith('shop-2');
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('sets server error when set default fails', () => {
    shopService.setDefaultShop.mockReturnValue(throwError(() => new Error('Failed')));
    const component = setup();

    component.onSetDefault('shop-2');

    expect(component.serverError()).toBe('Unable to set default store right now. Please try again.');
  });
});
