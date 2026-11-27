import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it } from 'vitest';

import { AdjustmentRowDto } from '../../services/inventory.models';
import { AdjustmentSummaryComponent } from './adjustment-summary.component';

describe('AdjustmentSummaryComponent', () => {
  const baseRows: AdjustmentRowDto[] = [
    {
      batchId: 'batch-1',
      direction: 'Increase',
      reason: 'FoundStock',
      quantity: 5,
      performedAt: '2026-05-05T08:30:00.000Z',
      notes: 'Stock counted',
    },
    {
      batchId: 'batch-2',
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 3,
      performedAt: '2026-05-05T09:00:00.000Z',
      notes: 'Damaged stock',
    },
  ];

  function setup(rows: AdjustmentRowDto[], loading = false) {
    TestBed.configureTestingModule({
      imports: [
        AdjustmentSummaryComponent,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              inventory: {
                summary: {
                  totalAdjustments: 'Total Adjustments',
                  increaseQuantity: 'Increase quantity',
                  decreaseQuantity: 'Decrease quantity',
                  netMovement: 'Net movement',
                  oneAdjustment: '1 adjustment',
                  multipleAdjustments: '{{count}} adjustments',
                  noNetMovement: 'No net movement',
                },
              },
            },
          },
          translocoConfig: {
            defaultLang: 'en',
            availableLangs: ['en'],
          },
          preloadLangs: true,
        }),
      ],
    });

    const fixture = TestBed.createComponent(AdjustmentSummaryComponent);
    fixture.componentInstance.rows = rows;
    fixture.componentInstance.loading = loading;
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders totals for increase and decrease rows', () => {
    const fixture = setup(baseRows);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('Total Adjustments');
    expect(host.textContent).toContain('5');
    expect(host.textContent).toContain('3');
    expect(host.textContent).toContain('+2.00');
    expect(fixture.componentInstance.netQuantity).toBe(2);
  });

  it('shows zero summary values when rows is empty', () => {
    const fixture = setup([]);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('No net movement');
    expect(host.querySelectorAll('.summary-card__value')[0]?.textContent?.trim()).toBe('0');
  });

  it('shows loading state when loading', () => {
    const fixture = setup([], true);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.summary-loading')).not.toBeNull();
    expect(host.querySelector('.summary-grid')).toBeNull();
  });

  it('keeps totals in sync with row updates', () => {
    const component = setup(baseRows).componentInstance;

    const updatedRows: AdjustmentRowDto[] = [...baseRows, { batchId: 'batch-3', direction: 'Increase', reason: 'FoundStock', quantity: 2, performedAt: null, notes: null }];
    component.rows = updatedRows;

    expect(component.totalRows).toBe(3);
    expect(component.increaseQuantity).toBe(7);
    expect(component.decreaseQuantity).toBe(3);
    expect(component.netQuantity).toBe(4);
  });
});
