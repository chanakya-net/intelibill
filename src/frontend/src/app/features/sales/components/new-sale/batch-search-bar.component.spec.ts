import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { BatchSearchBarComponent } from './batch-search-bar.component';

describe('BatchSearchBarComponent', () => {
  it('emits search term when search action is triggered', () => {
    TestBed.configureTestingModule({
      imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    const component = fixture.componentInstance;
    const searchSpy = vi.fn();
    const term = 'oreo';

    component.search.subscribe(searchSpy);
    component.searchTerm = term;
    fixture.detectChanges();

    component.emitSearch();
    expect(searchSpy).toHaveBeenCalledWith(term);
  });

  it('emits term changes', () => {
    TestBed.configureTestingModule({
      imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();

    component.searchTermChanged.subscribe(spy);
    component.onSearchTermChanged('batch-1');

    expect(spy).toHaveBeenCalledWith('batch-1');
  });

  it('emits filter queries for suggestions', () => {
    TestBed.configureTestingModule({
      imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();

    component.suggestionFilter.subscribe(spy);
    component.onComplete({ query: 'milk' } as never);

    expect(spy).toHaveBeenCalledWith('milk');
  });

  it('emits selected search suggestions', () => {
    TestBed.configureTestingModule({
      imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();

    component.suggestionSelected.subscribe(spy);
    component.onSuggestionSelected('barcode-1');

    expect(spy).toHaveBeenCalledWith('barcode-1');
  });

  it('emits openPicker when picker button clicked', () => {
    TestBed.configureTestingModule({
      imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();

    component.openPicker.subscribe(spy);
    component.onOpenPicker();

    expect(spy).toHaveBeenCalled();
  });
});
