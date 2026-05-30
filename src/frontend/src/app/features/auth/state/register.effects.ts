import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap, tap } from 'rxjs';

import { ApiErrorPayload } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { RegisterActions } from './register.actions';

@Injectable()
export class RegisterEffects {
  private readonly actions$ = inject(Actions);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly register$ = createEffect(() =>
    this.actions$.pipe(
      ofType(RegisterActions.requested),
      switchMap(({ firstName, lastName, email, phoneNumber, password, rememberMe }) =>
        this.authService.registerWithEmail(firstName, lastName, email, phoneNumber, password, rememberMe).pipe(
          map(() => RegisterActions.succeeded()),
          catchError((error: { error?: ApiErrorPayload }) =>
            of(
              RegisterActions.failed({
                errorMessage: getRegisterErrorMessage(error.error),
              })
            )
          )
        )
      )
    )
  );

  readonly navigateOnSuccess$ = createEffect(
    () =>
      this.actions$.pipe(
        ofType(RegisterActions.succeeded),
        tap(() => {
          void this.router.navigateByUrl('/');
        })
      ),
    { dispatch: false }
  );
}

function getRegisterErrorMessage(error: ApiErrorPayload | undefined): string {
  const title = error?.title ?? '';

  if (title === 'Auth.EmailAlreadyInUse') {
    return 'errors.auth.emailAlreadyInUse';
  }

  if (title === 'Auth.PhoneAlreadyInUse') {
    return 'errors.auth.phoneAlreadyInUseByAnother';
  }

  return 'errors.auth.unableToCreateAccount';
}
