import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, OnChanges, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AutoComplete, AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TranslocoPipe } from '@ngneat/transloco';

let batchSearchSequence = 0;

@Component({
  selector: 'app-batch-search-bar',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    AutoCompleteModule,
    ButtonModule,
    InputGroupModule,
    InputGroupAddonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './batch-search-bar.component.html',
  styleUrl: './batch-search-bar.component.scss',
})
export class BatchSearchBarComponent implements OnChanges {
  readonly searchInputId = `batch-search-${++batchSearchSequence}`;
  @Input() pickerOpen = false;
  ngOnChanges(): void { if (this.pickerOpen) this.closeSuggestions(); }
  @Input() searchTerm = '';
  @Input() loading = false;
  @Input() searchSuggestions: string[] = [];

  @Output() search = new EventEmitter<string>();
  @Output() openPicker = new EventEmitter<void>();
  @Output() searchTermChanged = new EventEmitter<string>();
  @Output() suggestionFilter = new EventEmitter<string>();
  @Output() suggestionSelected = new EventEmitter<string>();
  @Output() cameraOpen = new EventEmitter<void>();

  private readonly autocomplete = viewChild(AutoComplete);
  private closeSuggestions(): void { this.autocomplete()?.hide(); this.autocomplete()?.inputEL?.nativeElement.blur(); }
  emitSearch(): void {
    this.closeSuggestions();
    this.search.emit(this.searchTerm);
  }

  onSearchTermChanged(value: string | null): void {
    this.searchTermChanged.emit((value ?? '').toString());
  }

  onComplete(event: AutoCompleteCompleteEvent): void {
    if (this.pickerOpen) return;
    this.suggestionFilter.emit(event.query);
  }

  onSuggestionSelected(value: string): void {
    this.suggestionSelected.emit(value);
  }

  onEnterKey(): void {
    this.emitSearch();
  }

  onOpenPicker(): void {
    this.closeSuggestions();
    this.openPicker.emit();
  }

  openScanner(): void {
    this.cameraOpen.emit();
  }
}
