import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: integration', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('starts shop updates connection on init', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();

    expect(deps.shopUpdatesService.startConnection).toHaveBeenCalledTimes(1);
  });

  it('navigates away on cancel', () => {
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    vm.onCancel();
    expect(deps.router.navigate).toHaveBeenCalledWith(['/sales']);
  });
});

