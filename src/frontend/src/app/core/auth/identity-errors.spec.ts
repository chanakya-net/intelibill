import { describe, expect, it } from 'vitest';
import { problemCode } from '@intelibill/api';
import { identityError } from './identity-errors';

describe('identity errors', () => {
  it('reads public codes and title-only problem responses', () => {
    expect(problemCode({ code: 'Auth.InvalidCredentials' })).toBe('Auth.InvalidCredentials');
    expect(problemCode({ title: 'Auth.InvalidChallenge' })).toBe('Auth.InvalidChallenge');
    expect(identityError({ error: { title: 'Auth.InvalidChallenge' } })).toBe(
      'identity.invalidLink',
    );
  });

  it('preserves code precedence and ignores unrelated responses', () => {
    expect(
      identityError({ error: { code: 'Auth.InvalidCredentials', title: 'Auth.InvalidChallenge' } }),
    ).toBe('errors.auth.invalidCredentials');
    expect(identityError({ error: { code: '', title: 'Auth.InvalidChallenge' } })).toBe(
      'identity.tryAgain',
    );
    expect(problemCode('<html>')).toBeNull();
    expect(problemCode({ title: ' ' })).toBeNull();
  });
});
