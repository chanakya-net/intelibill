import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges, signal } from '@angular/core';

import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-shop-logo-upload',
  standalone: true,
  imports: [CommonModule, ButtonModule],
  templateUrl: './shop-logo-upload.component.html',
})
export class ShopLogoUploadComponent implements OnInit, OnChanges, OnDestroy {
  @Input() currentLogoUrl: string | null = null;
  @Input() disabled = false;
  @Output() readonly logoSelected = new EventEmitter<File>();
  @Output() readonly logoRemoved = new EventEmitter<void>();

  readonly previewUrl = signal('');
  readonly selectedFileName = signal('');

  private objectUrl: string | null = null;

  ngOnInit(): void {
    this.setPreview(this.currentLogoUrl);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['currentLogoUrl']) this.setPreview(this.currentLogoUrl);
  }

  ngOnDestroy(): void {
    this.clearObjectUrl();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.logoSelected.emit(file);
    this.selectedFileName.set(file.name);
    this.setPreviewFromFile(file);
  }

  onRemoveLogo(fileInput: HTMLInputElement): void {
    this.logoRemoved.emit();
    this.selectedFileName.set('');
    this.setPreview(null);
    fileInput.value = '';
  }

  private setPreviewFromFile(file: File): void {
    this.clearObjectUrl();
    if (typeof URL.createObjectURL === 'function') {
      this.objectUrl = URL.createObjectURL(file);
      this.previewUrl.set(this.objectUrl);
      return;
    }
    this.previewUrl.set('');
  }

  private setPreview(url: string | null): void {
    this.clearObjectUrl();
    this.previewUrl.set(url ?? '');
  }

  private clearObjectUrl(): void {
    if (!this.objectUrl) return;
    if (typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = null;
  }
}
