/**
 * The device flag that chooses the offline pack. `service-worker/protected-api-worker.js` carries
 * the same values; `scripts/tests/offline-pack-worker.spec.ts` keeps them in step.
 *
 * The page writes one word, `on` or `off`, into this cache. Before Angular's worker reads its
 * manifest, the wrapper looks the word up: `off` gives it `ngsw-lite.json` (nothing downloaded in
 * the background), anything else, including no flag at all, the full `ngsw.json`. The name is not
 * `ngsw:`-prefixed, so Angular never deletes it, and it holds no user or shop data.
 * See docs/frontend-architecture.md § Offline Pack.
 */
export const OFFLINE_PACK_STATE_CACHE = 'intelibill:offline-pack:state';

/** The key the flag is stored under, resolved against the worker script (the app's base). */
export const OFFLINE_PACK_STATE_KEY = 'intelibill-offline-pack-state';

export type OfflinePackFlag = 'on' | 'off';

/** The manifest of the full pack and the one a device without offline billing installs. */
export const FULL_MANIFEST_PATH = 'ngsw.json';
export const LITE_MANIFEST_PATH = 'ngsw-lite.json';

/**
 * The hash Angular's worker files a manifest under: SHA-1 of `JSON.stringify(manifest)`, taking the
 * low byte of each UTF-16 code unit (`ngsw-worker.js`, `hashManifest` / `sha1`). Manifests are ASCII,
 * where that equals UTF-8.
 */
export async function angularManifestHash(manifest: unknown): Promise<string> {
  const text = JSON.stringify(manifest);
  const bytes = new Uint8Array(text.length);
  for (let index = 0; index < text.length; index += 1) {
    bytes[index] = text.charCodeAt(index) & 0xff;
  }
  const digest = await crypto.subtle.digest('SHA-1', bytes);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}
