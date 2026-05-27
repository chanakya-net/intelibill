import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

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

  it('emits search updates when the search input changes', () => {
    const spy = vi.spyOn(component.searchValueChange, 'emit');
    fixture.detectChanges();

    const searchInput = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    searchInput.value = 'new product';
    searchInput.dispatchEvent(new Event('input'));

    expect(spy).toHaveBeenCalledWith('new product');
  });

  it('includes all inventory status filter options', () => {
    expect(component.statusOptions.length).toBe(6);
    expect(component.statusOptions.map((option) => option.value)).toEqual([
      'all',
      'active',
      'inactive',
      'inStock',
      'reorder',
      'outOfStock',
    ]);
  });

  it('emits status selection change', () => {
    const spy = vi.spyOn(component.statusFilterChange, 'emit');
    fixture.detectChanges();

    component.onStatusChange('outOfStock');

    expect(spy).toHaveBeenCalledWith('outOfStock');
  });
});
