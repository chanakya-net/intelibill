import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

interface NgswDataGroup {
  readonly name: string;
  readonly urls: readonly string[];
  readonly cacheConfig: {
    readonly strategy: string;
    readonly maxSize: number;
    readonly maxAge: string;
    readonly timeout: string;
  };
}

interface NgswConfig {
  readonly dataGroups: readonly NgswDataGroup[];
}

describe('ngsw-config offline sales API safeguards', () => {
  const config = JSON.parse(readFileSync(resolve(process.cwd(), 'ngsw-config.json'), 'utf8')) as NgswConfig;

  it('keeps offline correctness endpoints in no-cache data groups before broad API freshness cache', () => {
    const broadApiIndex = config.dataGroups.findIndex((group) => group.urls.includes('/api/**'));
    expect(broadApiIndex).toBeGreaterThan(-1);

    const criticalUrls = [
      '/api/ping',
      '/api/sales/offline-snapshot/stream',
      '/api/sales/invoice-leases/reserve',
      '/api/sales/offline-sync',
    ];

    for (const url of criticalUrls) {
      const groupIndex = config.dataGroups.findIndex((group) => group.urls.includes(url));
      expect(groupIndex, `${url} should have an explicit no-cache group`).toBeGreaterThan(-1);
      expect(groupIndex, `${url} group should be checked before /api/**`).toBeLessThan(broadApiIndex);
    }
  });

  it('uses zero-age freshness cache settings for offline correctness endpoints', () => {
    const noCacheGroups = config.dataGroups.filter((group) =>
      group.urls.some((url) =>
        [
          '/api/ping',
          '/api/sales/offline-snapshot/stream',
          '/api/sales/invoice-leases/reserve',
          '/api/sales/offline-sync',
        ].includes(url)
      )
    );

    expect(noCacheGroups.length).toBeGreaterThan(0);
    for (const group of noCacheGroups) {
      expect(group.cacheConfig).toEqual(
        expect.objectContaining({
          strategy: 'freshness',
          maxSize: 1,
          maxAge: '0s',
        }),
      );
    }
  });
});
