import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { CustomersFilterBarComponent } from './customers-filter-bar.component';

describe('CustomersFilterBarComponent', () => {
  let fixture: ComponentFixture<CustomersFilterBarComponent>;
  let component: CustomersFilterBarComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomersFilterBarComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersFilterBarComponent);
    component = fixture.componentInstance;
    component.searchValue = 'alice';
    component.statusFilter = 'active';
  });

  it('renders with provided inputs', () => {
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const searchInput = host.querySelector('input[type="text"]') as HTMLInputElement;
    const statusValue = host.querySelector('[data-status-filter-value]') as HTMLElement | null;
    const renderedSearchValue = searchInput.getAttribute('ng-reflect-model') ?? searchInput.value;

    expect(renderedSearchValue).toBe('alice');
    expect(statusValue?.textContent).toBe('active');
  });
});
