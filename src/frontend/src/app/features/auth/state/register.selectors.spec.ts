import { RegisterState } from '../../../core/state/app.state';
import { selectRegisterErrorMessage, selectRegisterSubmitting } from './register.selectors';

function buildState(overrides: Partial<RegisterState> = {}): { authRegistration: RegisterState } {
  return { authRegistration: { submitting: false, errorMessage: '', ...overrides } };
}

describe('register selectors', () => {
  it('selectRegisterSubmitting returns submitting flag', () => {
    expect(selectRegisterSubmitting(buildState({ submitting: true }))).toBe(true);
  });

  it('selectRegisterErrorMessage returns errorMessage', () => {
    expect(selectRegisterErrorMessage(buildState({ errorMessage: 'err' }))).toBe('err');
  });

  it('selectRegisterSubmitting returns false by default', () => {
    expect(selectRegisterSubmitting(buildState())).toBe(false);
  });
});
