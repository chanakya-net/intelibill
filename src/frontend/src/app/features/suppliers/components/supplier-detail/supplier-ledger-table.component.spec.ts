import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, describe, expect, it } from 'vitest';

import { SupplierLedgerTableComponent } from './supplier-ledger-table.component';

describe('SupplierLedgerTableComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('formats signed amounts', () => {
    TestBed.configureTestingModule({
      imports: [
        SupplierLedgerTableComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SupplierLedgerTableComponent);
    const component = fixture.componentInstance;

    expect(component.formatSignedAmount(100)).toMatch(/^\+/);
    expect(component.formatSignedAmount(-100)).toMatch(/^\-/);
  });
});

