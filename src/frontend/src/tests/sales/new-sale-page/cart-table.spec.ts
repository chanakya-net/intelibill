import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: cart table', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('toggles per-line discount editor state', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.onCartTableLineDiscountEditorToggled('clk-1');
    expect(vm.openLineDiscountEditorByKey()['clk-1']).toBe(true);

    vm.onCartTableLineDiscountEditorToggled('clk-1');
    expect(vm.openLineDiscountEditorByKey()['clk-1']).toBe(false);
  });
});
