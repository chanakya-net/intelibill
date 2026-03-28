import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { UpdateProfileOverlayComponent } from './update-profile-overlay.component';
import { UserAccountService } from '../services/user-account.service';

describe('UpdateProfileOverlayComponent', () => {
  const userAccountService = {
    updateMyProfile: vi.fn<UserAccountService['updateMyProfile']>(),
  };

  function setup(): UpdateProfileOverlayComponent {
    TestBed.configureTestingModule({
      imports: [UpdateProfileOverlayComponent],
      providers: [{ provide: UserAccountService, useValue: userAccountService }],
    });

    const fixture = TestBed.createComponent(UpdateProfileOverlayComponent);
    fixture.componentRef.setInput('user', {
      id: 'user-1',
      email: 'user@example.com',
      phoneNumber: '+15551234567',
      firstName: 'Test',
      lastName: 'User',
    });
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    userAccountService.updateMyProfile.mockReset();
    userAccountService.updateMyProfile.mockReturnValue(of(void 0));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not submit when form is invalid', () => {
    const component = setup();
    component.form.controls.email.setValue('');

    component.onSubmit();

    expect(component.form.touched).toBe(true);
    expect(userAccountService.updateMyProfile).not.toHaveBeenCalled();
  });

  it('submits trimmed values and emits closeRequested on success', () => {
    const component = setup();
    const closeSpy = vi.fn();
    component.closeRequested.subscribe(closeSpy);

    component.form.controls.firstName.setValue('  Jane  ');
    component.form.controls.lastName.setValue('  Doe  ');
    component.form.controls.email.setValue('jane@example.com');
    component.form.controls.phoneNumber.setValue('+15557654321');

    component.onSubmit();

    expect(userAccountService.updateMyProfile).toHaveBeenCalledWith({
      firstName: 'Jane',
      lastName: 'Doe',
      email: 'jane@example.com',
      phoneNumber: '+15557654321',
    });
    expect(closeSpy).toHaveBeenCalledTimes(1);
  });

  it('maps email conflict into friendly error message', () => {
    userAccountService.updateMyProfile.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.EmailAlreadyInUse' } }))
    );
    const component = setup();

    component.onSubmit();

    expect(component.serverError()).toBe('This email is already used by another account.');
  });
});
