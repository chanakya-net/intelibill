/**
 * The data groups earlier builds declared over `/api`. Their caches hold answers given to whoever
 * was signed in when they were stored, so they are the only caches this application ever purges.
 */
export const LEGACY_PROTECTED_DATA_GROUPS = ['api-fresh', 'ping-no-cache', 'offline-sales-api-no-cache'] as const;

/**
 * Angular 21's names for a data group's storage: `ngsw:<scope>:<version>:data:<group>:cache` for the
 * responses and `ngsw:<scope>:db:<version>:data:<group>:{age,lru}` for their bookkeeping
 * (`ngsw-worker.js`, `DataGroup` and `CacheDatabase`). The version is the group's own `version`, not
 * the app hash, so it survives app updates.
 *
 * `public/app-worker.js` carries the same pattern; `scripts/tests/app-worker.spec.ts` keeps the two
 * in step.
 */
const LEGACY_PROTECTED_CACHE_NAME = new RegExp(
  `^ngsw:[^:]*:(?:db:)?\\d+:data:(?:${LEGACY_PROTECTED_DATA_GROUPS.join('|')}):(?:cache|age|lru)$`,
);

export function isLegacyProtectedCacheName(name: string): boolean {
  return LEGACY_PROTECTED_CACHE_NAME.test(name);
}
