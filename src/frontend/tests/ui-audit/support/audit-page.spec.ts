import { describe, expect, it } from 'vitest';

import { isBaselineUpdateEnabled } from './audit-page';

describe('audit baseline contract', () => {
  it('does not enable baseline writes during a normal run', () => {
    expect(isBaselineUpdateEnabled({})).toBe(false);
    expect(isBaselineUpdateEnabled({ UI_AUDIT_UPDATE_BASELINE: '0' })).toBe(false);
  });

  it('only enables baseline writes in the explicit baseline mode', () => {
    expect(isBaselineUpdateEnabled({ UI_AUDIT_UPDATE_BASELINE: '1' })).toBe(true);
  });
});
