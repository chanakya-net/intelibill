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
    component.searchValue = '';
    component.statusFilter = 'all';
  });

  it('renders with provided inputs', () => {
    fixture.detectChanges();
    expect(component.searchValue).toBe('');
    expect(component.statusFilter).toBe('all');
    expect(fixture.nativeElement.textContent).toContain('common.status');
  });
});
