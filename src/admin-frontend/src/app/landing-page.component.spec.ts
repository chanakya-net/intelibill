import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SESSION_PORT, type SessionState } from '@intelibill/api';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AdminSessionService, type SessionNotice } from './features/auth/admin-session.service';
import { LandingPageComponent } from './landing-page.component';

function render(notice: SessionNotice = 'none') {
  const state = signal<SessionState>('authenticated');
  const reason = signal<SessionNotice>(notice);
  const check = vi.fn(() => of('active' as const));
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SESSION_PORT, useValue: { state, capabilities: signal(['admin.invitations.manage']) } },
      { provide: AdminSessionService, useValue: { notice: reason, check } },
    ],
  });
  const view = TestBed.createComponent(LandingPageComponent);
  view.detectChanges();
  return { state, reason, check, view, page: view.nativeElement as HTMLElement };
}

describe('provider console landing', () => {
  it('clears grants after the session ends without consulting browser storage', () => {
    const readStorage = vi.spyOn(Storage.prototype, 'getItem');
    try {
      const { state, reason, view, page } = render();
      expect(page.querySelector('a[href="/admin-users/invite"]')).toBeTruthy();
      state.set('unauthenticated');
      reason.set('ended');
      view.detectChanges();
      expect(page.querySelector('a[href="/admin-users/invite"]')).toBeNull();
      expect(page.querySelector('[role="alert"]')?.textContent).toContain('Your provider session ended.');
      expect(page.querySelector('a[href="/auth/sign-in"]')).toBeTruthy();
      expect(readStorage).not.toHaveBeenCalled();
    } finally {
      readStorage.mockRestore();
    }
  });

  it('offers to continue after the access token lapses instead of signing out', () => {
    const { state, reason, check, view, page } = render();
    state.set('unauthenticated');
    reason.set('expired');
    view.detectChanges();
    expect(page.querySelector('a[href="/admin-users/invite"]')).toBeNull();
    expect(page.textContent).toContain('Your provider access paused.');
    page.querySelector<HTMLButtonElement>('button')!.click();
    expect(check).toHaveBeenCalledOnce();
  });

  it('hides grants and offers only a manual retry when renewal is unconfirmed', () => {
    const { state, reason, check, view, page } = render();
    state.set('unauthenticated');
    reason.set('uncertain');
    view.detectChanges();
    expect(page.querySelector('a[href="/admin-users/invite"]')).toBeNull();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('could not confirm your provider session');
    expect(page.querySelector('a[href="/auth/sign-in"]')).toBeNull();
    expect(check).not.toHaveBeenCalled();
    page.querySelector<HTMLButtonElement>('button')!.click();
    expect(check).toHaveBeenCalledOnce();
  });
});
