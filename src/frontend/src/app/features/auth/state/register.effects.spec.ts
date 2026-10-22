import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { RegisterActions } from './register.actions';
import { RegisterEffects } from './register.effects';
import { registerReducer } from './register.reducer';

describe('RegisterEffects', () => {
  let actions$: Subject<Action>;
  let effects: RegisterEffects;
  const authService = { registerWithEmail: vi.fn() };
  const router = { navigateByUrl: vi.fn() };
  beforeEach(() => {
    actions$ = new Subject<Action>();
    authService.registerWithEmail.mockReset();
    router.navigateByUrl.mockClear();
    TestBed.configureTestingModule({
      providers: [
        RegisterEffects,
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
        { provide: Actions, useValue: new Actions(actions$) },
      ],
    });
    effects = TestBed.inject(RegisterEffects);
  });
  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });
  it('registerSuccessShowsVerificationWithoutSession', async () => {
    authService.registerWithEmail.mockReturnValue(of({ message: 'generic' }));
    const output = firstValueFrom(effects.register$);
    const request = RegisterActions.requested({ email: 'a@b.com' });
    actions$.next(request);
    const action = await output;
    expect(action).toEqual(RegisterActions.succeeded());
    const state = registerReducer(registerReducer(undefined, request), action);
    expect(state.accepted).toBe(true);
    expect(state.email).toBe('a@b.com');
    expect(authService.registerWithEmail).toHaveBeenCalledWith('a@b.com', false);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
  it('resends to the explicit legacy-claim endpoint without exposing account existence', async () => {
    authService.registerWithEmail.mockReturnValue(of({ message: 'generic' }));
    const output = firstValueFrom(effects.register$);
    actions$.next(RegisterActions.requested({ email: 'a@b.com', claim: true }));
    await output;
    expect(authService.registerWithEmail).toHaveBeenCalledWith('a@b.com', true);
  });
  it('explains unavailable delivery without pretending mail was accepted', async () => {
    authService.registerWithEmail.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.EmailUnavailable' } })),
    );
    const output = firstValueFrom(effects.register$);
    actions$.next(RegisterActions.requested({ email: 'a@b.com' }));
    expect(await output).toEqual(
      RegisterActions.failed({ errorMessage: 'identity.deliveryUnavailable' }),
    );
  });
});
