import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SESSION_PORT, type SessionState } from '@intelibill/api';
import { describe, expect, it, vi } from 'vitest';
import { LandingPageComponent } from './landing-page.component';

describe('provider console landing', () => {
  it('clears grants after session expiry without consulting browser storage', () => {
    const readStorage = vi.spyOn(Storage.prototype, 'getItem');
    try {
      const state = signal<SessionState>('authenticated');
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: SESSION_PORT, useValue: { state, capabilities: signal(['admin.invitations.manage']) } },
        ],
      });
      const view = TestBed.createComponent(LandingPageComponent);
      view.detectChanges();

      const page: HTMLElement = view.nativeElement;
      expect(page.querySelector('a[href="/admin-users/invite"]')).toBeTruthy();
      state.set('unauthenticated');
      view.detectChanges();
      expect(page.querySelector('a[href="/admin-users/invite"]')).toBeNull();
      expect(page.querySelector('[role="alert"]')?.textContent).toContain('Your provider session ended.');
      expect(readStorage).not.toHaveBeenCalled();
    } finally {
      readStorage.mockRestore();
    }
  });
});
