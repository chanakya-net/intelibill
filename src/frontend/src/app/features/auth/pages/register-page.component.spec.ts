import { Signal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { RegisterActions } from '../state/register.actions';
import {
  selectRegisterErrorMessage,
  selectRegisterSubmitting,
  selectRegisterAccepted,
  selectRegisterEmail,
  selectRegisterClaim,
} from '../state/register.selectors';
import { RegisterPageComponent } from './register-page.component';

describe('RegisterPageComponent', () => {
  const dispatch = vi.fn();
  const accepted = signal(false);
  const store = {
    dispatch,
    selectSignal: (selector: unknown): Signal<unknown> => {
      if (selector === selectRegisterAccepted) return accepted;
      if (selector === selectRegisterEmail) return signal('ada@example.com');
      if (selector === selectRegisterClaim || selector === selectRegisterSubmitting)
        return signal(false);
      return signal('');
    },
  };
  function setup() {
    TestBed.configureTestingModule({
      imports: [RegisterPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [provideRouter([]), { provide: Store, useValue: store }],
    });
    const fixture = TestBed.createComponent(RegisterPageComponent);
    fixture.detectChanges();
    return fixture;
  }
  beforeEach(() => {
    dispatch.mockClear();
    accepted.set(false);
  });
  afterEach(() => TestBed.resetTestingModule());
  it('collects only an email before sending ownership proof', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    expect(Object.keys(component.form.controls)).toEqual(['email']);
    component.form.controls.email.setValue('ada@example.com');
    component.onSubmit();
    expect(dispatch).toHaveBeenCalledWith(
      RegisterActions.requested({ email: 'ada@example.com', claim: false }),
    );
    expect(fixture.nativeElement.querySelector('input[type="password"]')).toBeNull();
  });
  it('rejects malformed email and whitespace', () => {
    const component = setup().componentInstance;
    component.form.controls.email.setValue('   ');
    component.onSubmit();
    expect(component.form.invalid).toBe(true);
    expect(dispatch).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: RegisterActions.requested.type }),
    );
  });
  it('shows inbox acceptance and resends generically', () => {
    accepted.set(true);
    const fixture = setup();
    expect(fixture.nativeElement.textContent).toContain('identity.checkInbox');
    fixture.componentInstance.onResend();
    expect(dispatch).toHaveBeenCalledWith(
      RegisterActions.requested({ email: 'ada@example.com', claim: false }),
    );
  });
  it('legacy claim requires an explicit user action', () => {
    const component = setup().componentInstance;
    component.form.controls.email.setValue('ada@example.com');
    component.onClaim();
    expect(dispatch).toHaveBeenCalledWith(
      RegisterActions.requested({ email: 'ada@example.com', claim: true }),
    );
  });
});
