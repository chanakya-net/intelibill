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
    component.searchValue = '';
    component.statusFilter = 'all';
  });

  it('renders with provided inputs', () => {
    fixture.detectChanges();
    expect(component.searchValue).toBe('');
    expect(component.statusFilter).toBe('all');
  });
});
