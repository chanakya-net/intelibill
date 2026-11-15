import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AdminSessionService, type SessionNotice } from '../auth/admin-session.service';
import { AdminCapabilitiesApi, manageAdminAccess } from './admin-capabilities-api.service';
import { AdminCapabilitiesPageComponent } from './admin-capabilities-page.component';

const operator = {
  id: 'operator', email: 'operator@example.test', version: 51,
  capabilities: ['support'], mfaEnrolled: true, disabled: false, isCurrent: false,
};

function render(capabilities: string[] = [manageAdminAccess]) {
  const grants = signal(capabilities);
  const notice = signal<SessionNotice>('none');
  const check = vi.fn(() => of('active'));
  const list = vi.fn(() => of({ items: [operator], nextCursor: null }));
  const update = vi.fn(() => of(operator));
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AdminSessionService, useValue: { capabilities: grants, notice, check } },
      { provide: AdminCapabilitiesApi, useValue: { list, update } },
    ],
  });
  const view = TestBed.createComponent(AdminCapabilitiesPageComponent);
  view.detectChanges();
  const page = view.nativeElement as HTMLElement;
  const select = () => {
    const picker = page.querySelector<HTMLSelectElement>('#administrator')!;
    picker.value = operator.id;
    picker.dispatchEvent(new Event('change'));
    view.detectChanges();
  };
  const submit = (confirmed: boolean) => {
    page.querySelector<HTMLSelectElement>('#reason')!.value = 'access-review';
    page.querySelector<HTMLInputElement>('[name="confirmed"]')!.checked = confirmed;
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    view.detectChanges();
  };
  return { page, view, select, submit, list, update, check, grants, notice };
}

describe('administrator access editor', () => {
  it('submits individually selected grants with the loaded version and confirmation', () => {
    const { page, select, submit, update } = render();
    select();
    const boxes = [...page.querySelectorAll<HTMLInputElement>('[name="grant"]')];
    expect(boxes).toHaveLength(5);
    expect(boxes.filter((box) => box.checked).map((box) => box.value)).toEqual(['support']);
    page.querySelector<HTMLInputElement>('[value="security-administrator"]')!.checked = true;

    submit(false);
    expect(update).not.toHaveBeenCalled();
    submit(true);
    expect(update).toHaveBeenCalledWith('operator', {
      capabilities: ['support', 'security-administrator'], expectedVersion: 51,
      reason: 'access-review', confirmed: true, disable: false,
    });
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Administrator access saved.');
  });

  it('requires a new sign-in after stale MFA and clears the form when the session ends', () => {
    const { page, select, submit, update, grants, notice, view } = render();
    update.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 403, error: { code: 'Admin.RecentMfaRequired' },
    })));
    select();
    submit(true);
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('within five minutes');
    expect(page.querySelector('a[href="/auth/sign-in"]')).toBeTruthy();
    submit(true);
    expect(update).toHaveBeenCalledOnce();

    grants.set([]);
    notice.set('ended');
    view.detectChanges();
    expect(page.querySelector('form')).toBeNull();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('session ended');
  });

  it('discards conflicting choices and waits for an explicit reload', () => {
    const { page, select, submit, list, update, view } = render();
    update.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 409, error: { code: 'Admin.CapabilityVersionConflict' },
    })));
    select();
    submit(true);
    expect(page.querySelector('form')).toBeNull();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Reload before editing');
    expect(list).toHaveBeenCalledOnce();

    page.querySelector<HTMLButtonElement>('button')!.click();
    view.detectChanges();
    expect(list).toHaveBeenCalledTimes(2);
    expect(update).toHaveBeenCalledOnce();
  });

  it('does not load administrator rows for a Billing-only session', () => {
    const { page, list } = render(['billing']);
    expect(list).not.toHaveBeenCalled();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('You cannot manage administrator access.');
  });
});
