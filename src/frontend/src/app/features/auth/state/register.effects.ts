import { inject, Injectable } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, exhaustMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { identityError } from '../../../core/auth/identity-errors';
import { RegisterActions } from './register.actions';
@Injectable()
export class RegisterEffects {
  private readonly actions$ = inject(Actions);
  private readonly authService = inject(AuthService);
  readonly register$ = createEffect(() =>
    this.actions$.pipe(
      ofType(RegisterActions.requested),
      exhaustMap(({ email, claim }) =>
        this.authService.registerWithEmail(email, claim ?? false).pipe(
          map(() => RegisterActions.succeeded()),
          catchError((error) => of(RegisterActions.failed({ errorMessage: identityError(error) }))),
        ),
      ),
    ),
  );
}
