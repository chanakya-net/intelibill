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
    component.searchValue = 'paracetamol';
    component.statusFilter = 'active';
  });

  it('renders with provided inputs', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const searchInput = host.querySelector('input[type="text"]') as HTMLInputElement;
    const statusValue = host.querySelector('[data-status-filter-value]') as HTMLElement | null;
    const renderedSearchValue = searchInput.getAttribute('ng-reflect-model') ?? searchInput.value;

    expect(renderedSearchValue).toBe('paracetamol');
    expect(statusValue?.textContent).toBe('active');
  });
});
