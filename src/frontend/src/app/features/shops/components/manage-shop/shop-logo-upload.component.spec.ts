import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { ShopLogoUploadComponent } from './shop-logo-upload.component';

describe('ShopLogoUploadComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [ShopLogoUploadComponent] }));

  it('shows the current logo preview', () => {
    const fixture = TestBed.createComponent(ShopLogoUploadComponent);
    fixture.componentRef.setInput('currentLogoUrl', 'https://cdn.example.com/logo.png');
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('img') as HTMLImageElement).src).toContain('logo.png');
  });

  it('emits file selection and updates the preview', () => {
    const createObjectUrl = vi.fn(() => 'blob:logo');
    const revokeObjectUrl = vi.fn();
    const originalCreate = Object.getOwnPropertyDescriptor(URL, 'createObjectURL');
    const originalRevoke = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL');
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: createObjectUrl });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revokeObjectUrl });

    try {
      const fixture = TestBed.createComponent(ShopLogoUploadComponent);
      const selected: File[] = [];
      fixture.componentInstance.logoSelected.subscribe((file) => selected.push(file));
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
      const file = new File(['logo'], 'shop-logo.png', { type: 'image/png' });
      Object.defineProperty(input, 'files', { value: [file] });
      input.dispatchEvent(new Event('change'));

      expect(selected).toHaveLength(1);
      expect(selected[0].name).toBe('shop-logo.png');
      expect(fixture.componentInstance.previewUrl()).toBe('blob:logo');
    } finally {
      if (originalCreate) Object.defineProperty(URL, 'createObjectURL', originalCreate);
      else delete (URL as { createObjectURL?: unknown }).createObjectURL;
      if (originalRevoke) Object.defineProperty(URL, 'revokeObjectURL', originalRevoke);
      else delete (URL as { revokeObjectURL?: unknown }).revokeObjectURL;
    }
  });

  it('emits removal and clears the preview', () => {
    const fixture = TestBed.createComponent(ShopLogoUploadComponent);
    const removed = vi.fn();
    fixture.componentInstance.logoRemoved.subscribe(removed);
    fixture.componentRef.setInput('currentLogoUrl', 'https://cdn.example.com/logo.png');
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
    fixture.componentInstance.onRemoveLogo(input);

    expect(removed).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.previewUrl()).toBe('');
  });
});
