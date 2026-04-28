import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { TableFilterBarComponent } from './table-filter-bar.component';

describe('TableFilterBarComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        TableFilterBarComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });
  });

  it('filters table and emits value on search change', () => {
    const fixture = TestBed.createComponent(TableFilterBarComponent);
    const component = fixture.componentInstance;
    const table = {
      filterGlobal: vi.fn(),
      clear: vi.fn(),
    };
    const emitSpy = vi.spyOn(component.searchValueChange, 'emit');

    component.table = table as never;
    component.onSearchChange('milk');

    expect(emitSpy).toHaveBeenCalledWith('milk');
    expect(table.filterGlobal).toHaveBeenCalledWith('milk', 'contains');
  });

  it('clears table and emits empty value', () => {
    const fixture = TestBed.createComponent(TableFilterBarComponent);
    const component = fixture.componentInstance;
    const table = {
      filterGlobal: vi.fn(),
      clear: vi.fn(),
    };
    const emitSpy = vi.spyOn(component.searchValueChange, 'emit');

    component.table = table as never;
    component.onClear();

    expect(emitSpy).toHaveBeenCalledWith('');
    expect(table.clear).toHaveBeenCalledTimes(1);
  });
});
