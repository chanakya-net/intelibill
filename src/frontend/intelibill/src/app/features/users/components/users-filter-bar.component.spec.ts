import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { UsersFilterBarComponent } from './users-filter-bar.component';

describe('UsersFilterBarComponent', () => {
  let fixture: ComponentFixture<UsersFilterBarComponent>;
  let component: UsersFilterBarComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        UsersFilterBarComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            'en-IN': {
              common: { all: 'All' },
              users: {
                owner: 'Owner',
                manager: 'Manager',
                staff: 'Staff',
                role: 'Role',
                searchPlaceholder: 'Search users...',
              },
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

    fixture = TestBed.createComponent(UsersFilterBarComponent);
    component = fixture.componentInstance;
    component.searchValue = 'alice';
    component.roleFilter = 'manager';
  });

  it('renders the search placeholder and emits search changes', () => {
    const searchSpy = vi.fn();
    component.searchValueChange.subscribe(searchSpy);

    fixture.detectChanges();

    const searchInput = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    expect(searchInput.placeholder).toBe('Search users...');

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

  it('emits role filter changes', () => {
    const roleSpy = vi.fn();
    component.roleFilterChange.subscribe(roleSpy);

    component.onRoleChange('staff');

    expect(roleSpy).toHaveBeenCalledWith('staff');
  });
});
