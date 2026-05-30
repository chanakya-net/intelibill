import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { CustomersFilterBarComponent } from './customers-filter-bar.component';

describe('CustomersFilterBarComponent', () => {
  let fixture: ComponentFixture<CustomersFilterBarComponent>;
  let component: CustomersFilterBarComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        CustomersFilterBarComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            'en-IN': {
              common: { all: 'All', clear: 'Clear' },
              customers: { active: 'Active', inactive: 'Inactive', searchPlaceholder: 'Search customers by name, phone, or address...' },
            },
          },
          preloadLangs: true,
          translocoConfig: {
            availableLangs: ['en-IN'],
            defaultLang: 'en-IN',
            reRenderOnLangChange: true,
          },
        }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersFilterBarComponent);
    component = fixture.componentInstance;
    component.searchValue = 'alice';
    component.statusFilter = 'active';
  });

  it('renders the search placeholder and emits search changes', () => {
    const searchSpy = vi.fn();
    component.searchValueChange.subscribe(searchSpy);

    fixture.detectChanges();

    const searchInput = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    expect(searchInput.placeholder).toBe('Search customers by name, phone, or address...');

    searchInput.value = 'beta';
    searchInput.dispatchEvent(new Event('input'));

    expect(searchSpy).toHaveBeenCalledWith('beta');
  });

  it('clears the search and emits the empty string', () => {
    const searchSpy = vi.fn();
    component.searchValueChange.subscribe(searchSpy);

    fixture.detectChanges();
    component.clearSearch();

    expect(searchSpy).toHaveBeenCalledWith('');
  });
});
