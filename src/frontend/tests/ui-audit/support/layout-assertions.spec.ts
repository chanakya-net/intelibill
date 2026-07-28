import { describe, expect, it } from 'vitest';

import { AUDIT_VIEWPORTS, validateLayoutMetrics } from './layout-assertions';

describe('login audit layout contract', () => {
  it('defines deterministic mobile and desktop profiles', () => {
    expect(AUDIT_VIEWPORTS).toEqual([
      { name: 'mobile', width: 360, height: 800 },
      { name: 'desktop', width: 1440, height: 900 },
    ]);
  });

  it('reports viewport overflow and cards outside the viewport', () => {
    expect(
      validateLayoutMetrics({
        viewport: { width: 360, height: 800 },
        documentWidth: 361,
        card: { left: -1, right: 362, top: 0, bottom: 800 },
      }),
    ).toEqual([
      'document width 361 exceeds viewport width 360',
      'auth card is outside the viewport bounds',
    ]);
  });
});
