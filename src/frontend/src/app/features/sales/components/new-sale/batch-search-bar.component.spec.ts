import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { AutoComplete } from 'primeng/autocomplete';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { BatchSearchBarComponent } from './batch-search-bar.component';

describe('BatchSearchBarComponent', () => {
  afterEach(() => { TestBed.resetTestingModule(); vi.unstubAllGlobals(); });
  it('scanner_enter_only_searches and prevents the enclosing form default', () => {
    TestBed.configureTestingModule({ imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })] });
    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    fixture.componentInstance.searchTerm = '890123';
    const searches: string[] = [];
    fixture.componentInstance.search.subscribe(value => searches.push(value));
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    const event = new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', bubbles: true, cancelable: true });
    input.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(searches).toEqual(['890123']);
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', bubbles: true }));
    expect(searches).toEqual(['890123']);
  });

  it('keeps autocomplete selection as the only Enter intent when an option is focused', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    TestBed.configureTestingModule({ imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })] });
    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    const selected: string[] = []; const searches: string[] = [];
    fixture.componentInstance.suggestionSelected.subscribe(value => selected.push(value));
    fixture.componentInstance.search.subscribe(value => searches.push(value));
    fixture.componentInstance.searchSuggestions = ['Milk'];
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.focus();
    // The suggestions already loaded; opening that public widget state avoids a debounce/network fake.
    fixture.debugElement.query(By.directive(AutoComplete)).componentInstance.show();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', code: 'ArrowDown', bubbles: true, cancelable: true }));
    fixture.detectChanges(); await fixture.whenStable();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', code: 'ArrowDown', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', bubbles: true, cancelable: true }));
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', bubbles: true }));
    expect(selected).toEqual(['Milk']);
    expect(searches).toEqual([]);
  });

  it('searches on Enter when suggestions are open with no focused choice', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    TestBed.configureTestingModule({ imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })] });
    const fixture = TestBed.createComponent(BatchSearchBarComponent);
    const searches: string[] = [];
    fixture.componentInstance.searchTerm = 'milk';
    fixture.componentInstance.searchSuggestions = ['Milk'];
    fixture.componentInstance.search.subscribe(value => searches.push(value));
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.focus(); fixture.debugElement.query(By.directive(AutoComplete)).componentInstance.show();
    fixture.detectChanges(); await fixture.whenStable();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', bubbles: true, cancelable: true }));
    expect(searches).toEqual(['milk']);
  });
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

  it('leaves the autocomplete before opening a lot picker so suggestions cannot cover its choices', () => {
    TestBed.configureTestingModule({ imports: [BatchSearchBarComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })] });
    const fixture = TestBed.createComponent(BatchSearchBarComponent); fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.focus(); expect(document.activeElement).toBe(input);
    fixture.componentInstance.emitSearch();
    expect(document.activeElement).not.toBe(input);
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
