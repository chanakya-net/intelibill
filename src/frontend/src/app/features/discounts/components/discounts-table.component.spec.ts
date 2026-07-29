import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { DiscountsTableComponent } from './discounts-table.component';
import { DiscountRuleListItemDto } from '../services/discount.service';

describe('DiscountsTableComponent', () => {
  let fixture: ComponentFixture<DiscountsTableComponent>;
  let component: DiscountsTableComponent;

  const mockRules: readonly DiscountRuleListItemDto[] = [
    {
      id: 'd1',
      ruleType: 'SalePercentage',
      name: 'New Customer',
      isActive: true,
      startsAt: null,
      endsAt: null,
      createdAt: new Date().toISOString(),
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        DiscountsTableComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DiscountsTableComponent);
    component = fixture.componentInstance;
    component.listItems = mockRules;
    component.statusKey = () => 'discounts.status.active';
    component.statusSeverity = () => 'success';
    component.formatDate = (value: string | null) => value ?? '-';
  });

  it('renders list rows from input data', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('New Customer');
    expect(host.textContent).toContain('discounts.ruleType.SalePercentage');
  });

  it('marks the selected row and supports keyboard selection', () => {
    component.selectedRuleId = 'd1';
    fixture.detectChanges();
    const row = fixture.nativeElement.querySelector(
      '[data-testid="discounts-row-d1"]',
    ) as HTMLElement;
    const selected: string[] = [];
    component.selectRule.subscribe((id) => selected.push(id));

    row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    expect(row.getAttribute('aria-current')).toBe('true');
    expect(selected).toEqual(['d1']);
  });

  it('does not emit pagination outside valid bounds', () => {
    const pages: number[] = [];
    component.pageChange.subscribe((page) => pages.push(page));
    component.pageNumber = 1;
    component.pageSize = 20;
    component.totalCount = 21;

    component.onPreviousPage();
    component.onNextPage();
    component.pageNumber = 2;
    component.onNextPage();

    expect(pages).toEqual([2]);
  });

  it('renders an empty state when no rules match', () => {
    component.listItems = [];
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector(
      '[data-testid="discounts-empty"]',
    ) as HTMLElement;
    expect(emptyState).not.toBeNull();
    expect(emptyState.textContent).toContain('discounts.list.empty');
    expect(emptyState.textContent).not.toContain('discounts.detail.emptySubtitle');
  });
});
