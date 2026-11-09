import enIN from '../../../../public/assets/i18n/en-IN.json';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { NewSalePageComponent } from '../../../app/features/sales/pages/new-sale-page.component';
import { setupNewSalePageTestBed } from './test-helpers';

describe('new-sale-page: offline state', () => {
  let deps: ReturnType<typeof setupNewSalePageTestBed>['deps'];

  beforeEach(() => {
    deps = setupNewSalePageTestBed().deps;
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders actionable localized refresh guidance at the offline search error boundary', () => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    });
    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.componentInstance.vm.batchSearchError.set('expiry.offline.refresh');
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('app-batch-picker-dialog')?.previousElementSibling
        ?.textContent,
    ).toContain('Refresh the complete expiry catalog online before issuing a new invoice.');
    expect(
      fixture.nativeElement.querySelector('app-batch-picker-dialog')?.previousElementSibling
        ?.textContent,
    ).not.toContain('expiry.offline.refresh');
  });

  it('enters offline mode when API unreachable', () => {
    deps.networkStatus.canReachApi.set(false);

    const fixture = TestBed.createComponent(NewSalePageComponent);
    fixture.detectChanges();
    const vm = fixture.componentInstance.vm;

    expect(vm.isOfflineMode()).toBe(true);
  });
});
