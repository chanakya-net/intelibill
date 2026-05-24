import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { BatchesFilterBarComponent } from './batches-filter-bar.component';
import { BatchFilters } from '../../services/inventory.models';

describe('BatchesFilterBarComponent', () => {
  let fixture: ComponentFixture<BatchesFilterBarComponent>;
  let component: BatchesFilterBarComponent;

  const initialFilters: BatchFilters = {
    search: 'milk',
    status: 'all',
    fromDate: '',
    toDate: '',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BatchesFilterBarComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(BatchesFilterBarComponent);
    component = fixture.componentInstance;
    component.filters = initialFilters;
  });

  it('emits search update', () => {
    const emitted: BatchFilters[] = [];
    component.filtersChange.subscribe((value) => emitted.push(value));

    component.onSearchChange('abc');

    expect(emitted).toHaveLength(1);
    expect(emitted[0]).toMatchObject({ search: 'abc', status: 'all', fromDate: '', toDate: '' });
  });

  it('emits status update', () => {
    const emitted: BatchFilters[] = [];
    component.filtersChange.subscribe((value) => emitted.push(value));

    component.onStatusChange('voided');

    expect(emitted).toHaveLength(1);
    expect(emitted[0]).toMatchObject({ status: 'voided' });
  });

  it('emits clear filter update', () => {
    const emitted: BatchFilters[] = [];
    component.filtersChange.subscribe((value) => emitted.push(value));

    component.onClearFilters();

    expect(emitted).toHaveLength(1);
    expect(emitted[0]).toEqual({
      search: '',
      status: 'all',
      fromDate: '',
      toDate: '',
    });
  });
});
