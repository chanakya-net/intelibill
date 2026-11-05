import { describe, expect, it } from 'vitest';

import { cacheControlFor } from '../../src/server-cache-policy';

describe('cacheControlFor', () => {
  it.each([
    'index.html',
    'index.csr.html',
    'ngsw.json',
    'ngsw-worker.js',
    'ngsw-angular-worker.js',
    'safety-worker.js',
    'worker-basic.min.js',
    'manifest.webmanifest',
  ])('revalidates %s on every load', (file) => {
    expect(cacheControlFor(file)).toBe('no-cache');
  });

  it.each(['chunk-ABC123.js', 'main-DEF456.js', 'styles-GHI789.css', 'assets/i18n/en-IN.json', 'nested/ngsw-worker.js'])(
    'leaves %s to the static policy',
    (file) => {
      expect(cacheControlFor(file)).toBeNull();
    },
  );

  it('accepts Windows separators', () => {
    expect(cacheControlFor('ngsw.json')).toBe('no-cache');
    expect(cacheControlFor('assets\\i18n\\en-IN.json')).toBeNull();
  });
});
