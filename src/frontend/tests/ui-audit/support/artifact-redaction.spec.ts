import { describe, expect, it } from 'vitest';

import { redactArtifact } from './artifact-redaction';

describe('redactArtifact', () => {
  it('redacts credential-shaped fields and bearer tokens', () => {
    const value = redactArtifact({
      password: 'plain-password',
      accessToken: 'eyJhbGciOiJIUzI1NiJ9.payload.signature',
      headers: { authorization: 'Bearer secret-token' },
      safe: 'visible',
    });

    expect(value).toEqual({
      password: '[REDACTED]',
      accessToken: '[REDACTED]',
      headers: { authorization: '[REDACTED]' },
      safe: 'visible',
    });
  });

  it('redacts secrets embedded in HTML and JSON text', () => {
    expect(redactArtifact('<input value="password=secret-value"> Bearer abc123')).toBe(
      '<input value="password=[REDACTED]"> Bearer [REDACTED]',
    );
  });
});
