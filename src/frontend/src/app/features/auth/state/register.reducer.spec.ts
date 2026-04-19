import { RegisterActions } from './register.actions';
import { registerReducer } from './register.reducer';

describe('registerReducer', () => {
  const initial = registerReducer(undefined, { type: '@@INIT' } as never);

  it('initial state has submitting=false and empty errorMessage', () => {
    expect(initial.submitting).toBe(false);
    expect(initial.errorMessage).toBe('');
  });

  it('sets submitting on requested', () => {
    const next = registerReducer(initial, RegisterActions.requested({
      firstName: 'A', lastName: 'B', email: 'a@b.com', password: 'Pass1!', rememberMe: false,
    }));
    expect(next.submitting).toBe(true);
    expect(next.errorMessage).toBe('');
  });

  it('clears submitting on succeeded', () => {
    const submitting = registerReducer(initial, RegisterActions.requested({
      firstName: 'A', lastName: 'B', email: 'a@b.com', password: 'Pass1!', rememberMe: false,
    }));
    const next = registerReducer(submitting, RegisterActions.succeeded());
    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('');
  });

  it('sets errorMessage on failed', () => {
    const next = registerReducer(initial, RegisterActions.failed({ errorMessage: 'errors.auth.emailAlreadyInUse' }));
    expect(next.submitting).toBe(false);
    expect(next.errorMessage).toBe('errors.auth.emailAlreadyInUse');
  });

  it('clears errorMessage on clearError', () => {
    const withError = registerReducer(initial, RegisterActions.failed({ errorMessage: 'err' }));
    const next = registerReducer(withError, RegisterActions.clearError());
    expect(next.errorMessage).toBe('');
  });
});
