import { TestBed } from '@angular/core/testing';
import { SESSION_PORT } from '@intelibill/api';
import { describe, expect, it, vi } from 'vitest';
import { unavailableProviderSession } from './unavailable-provider-session';
import { LandingPageComponent } from './landing-page.component';

describe('provider console landing', () => {
  it('shows denial from its injected session without consulting browser storage', () => {
    const readStorage = vi.spyOn(Storage.prototype, 'getItem');
    try {
      TestBed.configureTestingModule({
        providers: [{ provide: SESSION_PORT, useValue: unavailableProviderSession }],
      });
      const view = TestBed.createComponent(LandingPageComponent);
      view.detectChanges();

      const page: HTMLElement = view.nativeElement;
      expect(page.querySelector('[role="alert"]')?.textContent).toContain('No provider session');
      expect(page.textContent).toContain('No provider permissions');
      expect(page.querySelector('input, a[href]')).toBeNull();
      expect(readStorage).not.toHaveBeenCalled();
    } finally {
      readStorage.mockRestore();
    }
  });
});
