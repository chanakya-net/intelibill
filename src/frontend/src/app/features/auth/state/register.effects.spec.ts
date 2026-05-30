import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Action } from '@ngrx/store';
import { Actions } from '@ngrx/effects';
import { Observable, Subject, firstValueFrom, of, throwError } from 'rxjs';
import { take } from 'rxjs/operators';
import { vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { RegisterActions } from './register.actions';
import { RegisterEffects } from './register.effects';

describe('RegisterEffects', () => {
  let actions$: Subject<Action>;
  let effects: RegisterEffects;

  const authService = {
    registerWithEmail: vi.fn<AuthService['registerWithEmail']>(),
  };
  const router = { navigateByUrl: vi.fn().mockResolvedValue(true) };

  beforeEach(() => {
    actions$ = new Subject<Action>();
    authService.registerWithEmail.mockReset();
    router.navigateByUrl.mockReset();

    TestBed.configureTestingModule({
      providers: [
        RegisterEffects,
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
        {
          provide: Actions,
          useFactory: (): Observable<Action> => new Actions(actions$),
        },
      ],
    });

    effects = TestBed.inject(RegisterEffects);
  });

  afterEach(() => {
    actions$.complete();
    TestBed.resetTestingModule();
  });

  const requestedPayload = {
    firstName: 'A',
    lastName: 'B',
    email: 'a@b.com',
    phoneNumber: '+15551234567',
    password: 'Pass1!',
    rememberMe: false,
  };

  it('dispatches succeeded on successful registration', async () => {
    const mockSession = { accessToken: 'tok', refreshToken: 'ref', accessTokenExpiresAt: '', refreshTokenExpiresAt: '', rememberMe: false };
    (authService.registerWithEmail as ReturnType<typeof vi.fn>).mockReturnValue(of(mockSession));

    const output = firstValueFrom(effects.register$.pipe(take(1)));
    actions$.next(RegisterActions.requested(requestedPayload));

    await expect(output).resolves.toEqual(RegisterActions.succeeded());
  });

  it('dispatches failed with emailAlreadyInUse error', async () => {
    authService.registerWithEmail.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.EmailAlreadyInUse' } }))
    );

    const output = firstValueFrom(effects.register$.pipe(take(1)));
    actions$.next(RegisterActions.requested(requestedPayload));

    await expect(output).resolves.toEqual(
      RegisterActions.failed({ errorMessage: 'errors.auth.emailAlreadyInUse' })
    );
  });

  it('dispatches failed with phoneAlreadyInUse error', async () => {
    authService.registerWithEmail.mockReturnValue(
      throwError(() => ({ error: { title: 'Auth.PhoneAlreadyInUse' } }))
    );

    const output = firstValueFrom(effects.register$.pipe(take(1)));
    actions$.next(RegisterActions.requested(requestedPayload));

    await expect(output).resolves.toEqual(
      RegisterActions.failed({ errorMessage: 'errors.auth.phoneAlreadyInUseByAnother' })
    );
  });

  it('dispatches failed with generic error for unknown titles', async () => {
    authService.registerWithEmail.mockReturnValue(
      throwError(() => ({ error: { title: 'Unknown.Error' } }))
    );

    const output = firstValueFrom(effects.register$.pipe(take(1)));
    actions$.next(RegisterActions.requested(requestedPayload));

    await expect(output).resolves.toEqual(
      RegisterActions.failed({ errorMessage: 'errors.auth.unableToCreateAccount' })
    );
  });

  it('navigates to / on success (navigateOnSuccess$)', async () => {
    const output = firstValueFrom(effects.navigateOnSuccess$.pipe(take(1)));
    actions$.next(RegisterActions.succeeded());
    await output;

    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });
});
