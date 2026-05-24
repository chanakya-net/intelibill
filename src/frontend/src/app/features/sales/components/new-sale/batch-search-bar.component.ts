import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputGroupModule } from 'primeng/inputgroup';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TranslocoPipe } from '@ngneat/transloco';

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
})
export class BatchSearchBarComponent {
  @Input() searchTerm = '';
  @Input() loading = false;
  @Input() searchSuggestions: string[] = [];

  @Output() search = new EventEmitter<string>();
  @Output() openPicker = new EventEmitter<void>();
  @Output() searchTermChanged = new EventEmitter<string>();
  @Output() suggestionFilter = new EventEmitter<string>();
  @Output() suggestionSelected = new EventEmitter<string>();
  @Output() cameraOpen = new EventEmitter<void>();

  emitSearch(): void {
    this.search.emit(this.searchTerm);
  }

  onSearchTermChanged(value: string | null): void {
    this.searchTermChanged.emit((value ?? '').toString());
  }

  onComplete(event: AutoCompleteCompleteEvent): void {
    this.suggestionFilter.emit(event.query);
  }

  onSuggestionSelected(value: string): void {
    this.suggestionSelected.emit(value);
  }

  onEnterKey(): void {
    this.emitSearch();
  }

  onOpenPicker(): void {
    this.openPicker.emit();
  }

  openScanner(): void {
    this.cameraOpen.emit();
  }
}
