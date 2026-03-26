import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Store } from '@ngrx/store';

import { finalize } from 'rxjs';

import { HttpUiActions } from '../state/http-ui.actions';

export const httpLoadingInterceptor: HttpInterceptorFn = (request, next) => {
  const store = inject(Store);

  store.dispatch(HttpUiActions.requestStarted());

  return next(request).pipe(
    finalize(() => {
      store.dispatch(HttpUiActions.requestEnded());
    })
  );
};
