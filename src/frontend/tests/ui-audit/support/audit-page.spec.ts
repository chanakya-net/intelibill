import { describe, expect, it, vi, afterEach } from 'vitest';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { mkdir, rm, writeFile, readFile } from 'node:fs/promises';

import {
  assertNoUnexpectedBrowserFailures,
  compareScreenshot,
  isBaselineUpdateEnabled,
  type BrowserFailure,
} from './audit-page';

describe('audit baseline contract', () => {
  it('does not enable baseline writes during a normal run', () => {
    expect(isBaselineUpdateEnabled({})).toBe(false);
    expect(isBaselineUpdateEnabled({ UI_AUDIT_UPDATE_BASELINE: '0' })).toBe(false);
  });

  it('only enables baseline writes in the explicit baseline mode', () => {
    expect(isBaselineUpdateEnabled({ UI_AUDIT_UPDATE_BASELINE: '1' })).toBe(true);
  });
});

describe('assertNoUnexpectedBrowserFailures', () => {
  it('throws when browser failures are present', () => {
    const failures: BrowserFailure[] = [
      { kind: 'console', message: 'error: something went wrong' },
    ];
    expect(() => assertNoUnexpectedBrowserFailures(failures)).toThrow(
      /unexpected browser failures/,
    );
  });

  it('passes when no failures are present', () => {
    expect(() => assertNoUnexpectedBrowserFailures([])).not.toThrow();
  });
});

describe('compareScreenshot baseline contract', () => {
  const testDir = join(tmpdir(), `vitest-audit-${Date.now()}`);

  afterEach(async () => {
    await rm(testDir, { recursive: true, force: true });
  });

  it('updates baseline when updateBaseline=true', async () => {
    const baselinePath = join(testDir, 'test-baseline.png');
    const buffer = Buffer.from('screenshot-data-1');

    const mockPage = {
      screenshot: vi.fn().mockResolvedValue(buffer),
      waitForTimeout: vi.fn().mockResolvedValue(undefined),
    };

    const result = await compareScreenshot({
      page: mockPage as any,
      baselinePath,
      updateBaseline: true,
    });

    expect(result.status).toBe('updated');
    expect(result.bytes).toEqual(buffer);
    const written = await readFile(baselinePath);
    expect(written).toEqual(buffer);
  });

  it('does not overwrite baseline when updateBaseline=false', async () => {
    const baselinePath = join(testDir, 'existing-baseline.png');
    const originalContent = Buffer.from('original-baseline');
    const newScreenshot = Buffer.from('new-screenshot-different');

    await mkdir(testDir, { recursive: true });
    await writeFile(baselinePath, originalContent);

    const mockPage = {
      screenshot: vi.fn().mockResolvedValue(newScreenshot),
    };

    await expect(
      compareScreenshot({
        page: mockPage as any,
        baselinePath,
        updateBaseline: false,
      }),
    ).rejects.toThrow(/screenshot differs from approved baseline/);

    const onDisk = await readFile(baselinePath);
    expect(onDisk).toEqual(originalContent);
  });

  it('passes when screenshot matches baseline', async () => {
    const baselinePath = join(testDir, 'matching-baseline.png');
    const screenshot = Buffer.from('matching-content');

    await mkdir(testDir, { recursive: true });
    await writeFile(baselinePath, screenshot);

    const mockPage = {
      screenshot: vi.fn().mockResolvedValue(screenshot),
    };

    const result = await compareScreenshot({
      page: mockPage as any,
      baselinePath,
      updateBaseline: false,
    });

    expect(result.status).toBe('matched');
    const onDisk = await readFile(baselinePath);
    expect(onDisk).toEqual(screenshot);
  });
});
