import { HttpRequest } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { HttpUiActions } from '../state/http-ui.actions';
import { httpLoadingInterceptor } from './http-loading.interceptor';

describe('httpLoadingInterceptor', () => {
  const dispatch = vi.fn();
  const store = { dispatch };

  beforeEach(() => {
    dispatch.mockReset();
    TestBed.configureTestingModule({
      providers: [{ provide: Store, useValue: store }],
    });
  });

  it('dispatches requestStarted on request and requestEnded on complete', async () => {
    const req = new HttpRequest('GET', '/api/test');
    const next = vi.fn().mockReturnValue(of({ type: 4 } as never));

    await new Promise<void>((resolve) => {
      TestBed.runInInjectionContext(() => {
        httpLoadingInterceptor(req, next).subscribe({ complete: () => setTimeout(resolve, 0) });
      });
    });

    expect(dispatch).toHaveBeenCalledWith(HttpUiActions.requestStarted());
    expect(dispatch).toHaveBeenCalledWith(HttpUiActions.requestEnded());
  });

  it('dispatches requestEnded even when request errors', async () => {
    const req = new HttpRequest('GET', '/api/test');
    const next = vi.fn().mockReturnValue(
      new Observable((observer) => { observer.error(new Error('fail')); })
    );

    await new Promise<void>((resolve) => {
      TestBed.runInInjectionContext(() => {
        httpLoadingInterceptor(req, next).subscribe({ error: () => setTimeout(resolve, 0) });
      });
    });

    expect(dispatch).toHaveBeenCalledWith(HttpUiActions.requestStarted());
    expect(dispatch).toHaveBeenCalledWith(HttpUiActions.requestEnded());
  });
});
