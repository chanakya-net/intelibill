import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: cart checkout summary', () => {
  beforeEach(() => {
    setupNewSalePageTestBed();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('toggles sale-level discount editor open state', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.isSaleDiscountEditorOpen()).toBe(false);
    vm.toggleSaleDiscountEditor();
    expect(vm.isSaleDiscountEditorOpen()).toBe(true);
  });
});

