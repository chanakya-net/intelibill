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
      switchMap(({ firstName, lastName, email, password, rememberMe }) =>
        this.authService.registerWithEmail(firstName, lastName, email, password, rememberMe).pipe(
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
    return 'An account with this email already exists.';
  }

  if (error?.detail) {
    return error.detail;
  }

  return 'Unable to create your account right now. Please try again.';
}
