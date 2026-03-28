import { signal, Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { vi } from 'vitest';

import { SetDefaultStoreOverlayComponent } from './set-default-store-overlay.component';
import { ShopsActions } from '../../shops/state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsSubmitting,
} from '../../shops/state/shops.selectors';

describe('SetDefaultStoreOverlayComponent', () => {
  const dispatch = vi.fn();
  const isSubmittingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationTypeSignal = signal<'create' | 'update' | 'set-default' | null>(null);
  const lastMutationSucceededSignal = signal(false);

  const store = {
    dispatch,
    selectSignal: vi.fn((selector: unknown): Signal<unknown> => {
      if (selector === selectShopsSubmitting) {
        return isSubmittingSignal;
      }

      if (selector === selectShopsErrorMessage) {
        return errorSignal;
      }

      if (selector === selectShopsLastMutationType) {
        return lastMutationTypeSignal;
      }

      if (selector === selectShopsLastMutationSucceeded) {
        return lastMutationSucceededSignal;
      }

      return signal(undefined);
    }),
  };

  function setup(): { component: SetDefaultStoreOverlayComponent; fixture: ReturnType<typeof TestBed.createComponent<SetDefaultStoreOverlayComponent>> } {
    TestBed.configureTestingModule({
      imports: [SetDefaultStoreOverlayComponent],
      providers: [{ provide: Store, useValue: store }],
    });

    const fixture = TestBed.createComponent(SetDefaultStoreOverlayComponent);
    fixture.componentRef.setInput('activeShopId', 'shop-1');
    fixture.componentRef.setInput('shops', [
      { shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null },
      { shopId: 'shop-2', shopName: 'Branch', role: 'Manager', isDefault: false, lastUsedAt: null },
    ]);
    fixture.detectChanges();
    return { component: fixture.componentInstance, fixture };
  }

  beforeEach(() => {
    dispatch.mockReset();
    store.selectSignal.mockClear();
    isSubmittingSignal.set(false);
    errorSignal.set('');
    lastMutationTypeSignal.set(null);
    lastMutationSucceededSignal.set(false);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('closes directly when selecting currently active shop', () => {
    const { component } = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.onSetDefault('shop-1');

    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: ShopsActions.setDefaultShopRequested.type })
    );
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('dispatches setDefault action and closes on success', () => {
    const { component, fixture } = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.onSetDefault('shop-2');

    expect(dispatch).toHaveBeenCalledWith(ShopsActions.setDefaultShopRequested({ shopId: 'shop-2' }));

    lastMutationTypeSignal.set('set-default');
    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('reads server error from selector', () => {
    const { component } = setup();
    errorSignal.set('Unable to set default store right now. Please try again.');

    expect(component.serverError()).toBe('Unable to set default store right now. Please try again.');
  });
});
