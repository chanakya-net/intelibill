import { spawnSync } from 'node:child_process';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

describe('run-ui-audit', () => {
  it('keeps Vitest unit specs out of baseline discovery when Playwright flags are forwarded', () => {
    const frontendRoot = join(import.meta.dir, '..', '..');
    const result = spawnSync(
      process.execPath,
      ['run', 'scripts/run-ui-audit.mjs', '--baseline', '--update-snapshots', '--list'],
      {
        cwd: frontendRoot,
        encoding: 'utf8',
      },
    );

    expect(result.stderr).not.toContain('Vitest cannot be imported in a CommonJS module');
    expect(result.status).toBe(0);
  });
});
