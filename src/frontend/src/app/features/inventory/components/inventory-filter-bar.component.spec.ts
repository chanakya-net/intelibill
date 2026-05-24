import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { InventoryFilterBarComponent } from './inventory-filter-bar.component';

describe('InventoryFilterBarComponent', () => {
  let fixture: ComponentFixture<InventoryFilterBarComponent>;
  let component: InventoryFilterBarComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InventoryFilterBarComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(InventoryFilterBarComponent);
    component = fixture.componentInstance;
    component.searchValue = '';
    component.statusFilter = 'all';
  });

  it('renders with provided inputs', () => {
    fixture.detectChanges();
    expect(component.searchValue).toBe('');
    expect(component.statusFilter).toBe('all');
  });
});
