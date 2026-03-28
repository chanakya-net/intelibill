import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { ChangePasswordOverlayComponent } from './change-password-overlay.component';
import { UserAccountService } from '../services/user-account.service';

describe('ChangePasswordOverlayComponent', () => {
  const userAccountService = {
    changeMyPassword: vi.fn<UserAccountService['changeMyPassword']>(),
  };

  function setup(): ChangePasswordOverlayComponent {
    TestBed.configureTestingModule({
      imports: [ChangePasswordOverlayComponent],
      providers: [{ provide: UserAccountService, useValue: userAccountService }],
    });

    const fixture = TestBed.createComponent(ChangePasswordOverlayComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    userAccountService.changeMyPassword.mockReset();
    userAccountService.changeMyPassword.mockReturnValue(of(void 0));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when form is invalid', () => {
    const component = setup();
    component.form.controls.currentPassword.setValue('');
    component.form.controls.newPassword.setValue('short');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(userAccountService.changeMyPassword).not.toHaveBeenCalled();
  });

  it('submits and emits closeRequested on success', () => {
    const component = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.form.controls.currentPassword.setValue('OldPass123!');
    component.form.controls.newPassword.setValue('NewPass123!');

    component.onSubmit();

    expect(userAccountService.changeMyPassword).toHaveBeenCalledWith({
      currentPassword: 'OldPass123!',
      newPassword: 'NewPass123!',
    });
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('maps invalid current password into friendly error', () => {
    userAccountService.changeMyPassword.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.InvalidCurrentPassword' } }))
    );
    const component = setup();

    component.form.controls.currentPassword.setValue('WrongPass!');
    component.form.controls.newPassword.setValue('NewPass123!');
    component.onSubmit();

    expect(component.serverError()).toBe('Current password is incorrect.');
  });
});
