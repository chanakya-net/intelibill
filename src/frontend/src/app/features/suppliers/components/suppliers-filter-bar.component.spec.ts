import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { SuppliersFilterBarComponent } from './suppliers-filter-bar.component';

describe('SuppliersFilterBarComponent', () => {
  let fixture: ComponentFixture<SuppliersFilterBarComponent>;
  let component: SuppliersFilterBarComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SuppliersFilterBarComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(SuppliersFilterBarComponent);
    component = fixture.componentInstance;
    component.searchValue = 'acme';
    component.statusFilter = 'active';
  });

  it('renders with provided inputs', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const searchInput = host.querySelector('input[type="text"]') as HTMLInputElement;
    const statusValue = host.querySelector('[data-status-filter-value]') as HTMLElement | null;
    const renderedSearchValue = searchInput.getAttribute('ng-reflect-model') ?? searchInput.value;

    expect(renderedSearchValue).toBe('acme');
    expect(statusValue?.textContent).toBe('active');
  });
});
