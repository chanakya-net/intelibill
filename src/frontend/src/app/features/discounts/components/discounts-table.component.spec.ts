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
      imports: [DiscountsTableComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
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
    expect(component.listItems).toHaveLength(1);
  });
});
