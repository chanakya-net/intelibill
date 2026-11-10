/* intelibill:protected-api-worker */
/*
 * Intelibill's service worker: Angular's worker, with protected traffic kept out of it.
 *
 * The build publishes this file as `ngsw-worker.js` and Angular's own worker beside it as
 * `ngsw-angular-worker.js` (scripts/finalize-service-worker.mjs). Keeping the URL every earlier
 * build registered matters: the browser's own update check of that URL installs this worker, and
 * an older page registering `ngsw-worker.js` again cannot bring Angular's bare worker back.
 *
 * Every answer under /api and /hubs belongs to the user and shop that asked for it. Earlier builds
 * let Angular's worker cache those answers by URL and serve them when the network was slow, which
 * handed one shop's data to another. A tab keeps the app version it loaded with, and that
 * version's rules, until it reloads, so changing the configuration alone leaves such a tab
 * exposed. Once this worker is installed, Angular's code in it takes control of every open tab
 * (skipWaiting + clients.claim), and the listeners below run before Angular's for all of them.
 *
 * See docs/frontend-architecture.md § Protected API Cache Isolation.
 *
 * It also decides which manifest Angular's worker sees, so that only a device with offline billing
 * downloads the app for offline use. The build writes `ngsw.json` (the full offline pack) and
 * `ngsw-lite.json` (nothing to download, navigations from the network). When the page has written
 * an explicit "off" into the `intelibill:offline-pack:state` cache, Angular's request for
 * `ngsw.json` is answered with the lite manifest; anything else, including no flag at all, keeps
 * the full one, so a device enabled before this existed keeps its pack. Every other request passes
 * through untouched. See docs/frontend-architecture.md § Offline Pack.
 */
'use strict';

(function () {
  var PROTECTED_PATH_PREFIXES = ['/api/', '/hubs/'];

  // Same values as src/app/core/service-worker/service-worker-registration.ts.
  var ISOLATION_QUERY = 'intelibill:protected-api-isolation';
  var ISOLATION_PROTOCOL = 1;

  // Same pattern as src/app/core/service-worker/protected-cache-names.ts (kept in step by
  // scripts/tests/protected-api-worker.spec.ts): the storage of the data groups earlier builds
  // declared over /api.
  var LEGACY_PROTECTED_CACHE_NAME =
    /^ngsw:[^:]*:(?:db:)?\d+:data:(?:api-fresh|ping-no-cache|offline-sales-api-no-cache):(?:cache|age|lru)$/;

  function isProtectedRequest(request) {
    var url;
    try {
      url = new URL(request.url);
    } catch (error) {
      return false;
    }

    if (url.origin !== self.location.origin) {
      return false;
    }

    return PROTECTED_PATH_PREFIXES.some(function (prefix) {
      return url.pathname.indexOf(prefix) === 0;
    });
  }

  // Registered before Angular's listener. Stopping the event without respondWith() leaves the
  // request to the browser's own network handling, so no rule of any app version can answer it.
  self.addEventListener('fetch', function (event) {
    if (isProtectedRequest(event.request)) {
      event.stopImmediatePropagation();
    }
  });

  // Lets a page tell this worker from an earlier one, which shares its URL: only this one answers.
  self.addEventListener('message', function (event) {
    if (!event.data || event.data.action !== ISOLATION_QUERY) {
      return;
    }

    event.stopImmediatePropagation();
    var port = event.ports && event.ports[0];
    if (port) {
      port.postMessage({ protectedApiIsolation: ISOLATION_PROTOCOL });
    }
  });

  // Empties each cache before deleting it: an old worker still running may hold a handle to the
  // cache it opened, and deleting the name alone would leave that handle's entries readable.
  function purgeLegacyProtectedCaches() {
    return self.caches.keys().then(function (names) {
      return Promise.all(
        names
          .filter(function (name) {
            return LEGACY_PROTECTED_CACHE_NAME.test(name);
          })
          .map(function (name) {
            return self.caches
              .open(name)
              .then(function (cache) {
                return cache.keys().then(function (requests) {
                  return Promise.all(
                    requests.map(function (request) {
                      return cache.delete(request);
                    }),
                  );
                });
              })
              .then(function () {
                return self.caches.delete(name);
              });
          }),
      );
    });
  }

  // Same values as src/app/core/service-worker/offline-pack-flag.ts. The cache name is not
  // `ngsw:`-prefixed, so Angular's cache clean-up never removes it, and it holds one word, never
  // user or shop data.
  var OFFLINE_PACK_STATE_CACHE = 'intelibill:offline-pack:state';
  var OFFLINE_PACK_STATE_KEY = 'intelibill-offline-pack-state';
  var OFFLINE_PACK_OFF = 'off';
  var FULL_MANIFEST = 'ngsw.json';
  var LITE_MANIFEST = 'ngsw-lite.json';

  /** The flag the page wrote, or null when there is none or it cannot be read (both mean full). */
  function readOfflinePackState() {
    var storage = self.caches;
    if (!storage || typeof storage.match !== 'function') {
      return Promise.resolve(null);
    }

    return Promise.resolve()
      .then(function () {
        return storage.match(new URL(OFFLINE_PACK_STATE_KEY, self.location.href).href, {
          cacheName: OFFLINE_PACK_STATE_CACHE,
        });
      })
      .then(function (response) {
        return response ? response.text() : null;
      })
      .then(
        function (text) {
          return typeof text === 'string' ? text.trim() : null;
        },
        function () {
          return null;
        },
      );
  }

  function requestUrl(input) {
    try {
      return new URL(typeof input === 'string' ? input : String(input && input.url ? input.url : input), self.location.href);
    } catch (error) {
      return null;
    }
  }

  function requestMethod(input, init) {
    var method = (init && init.method) || (input && typeof input === 'object' && input.method) || 'GET';
    return String(method).toUpperCase();
  }

  // Angular's worker asks for its manifest as `self.fetch('ngsw.json?ngsw-cache-bust=…')`, looking
  // `fetch` up on `self` at call time (pinned by scripts/tests/angular-worker-manifest.spec.ts), so
  // replacing it here, before Angular's script loads, decides which manifest it installs.
  var networkFetch = self.fetch;
  if (typeof networkFetch === 'function') {
    var fullManifestUrl = new URL(FULL_MANIFEST, self.location.href);

    self.fetch = function (input, init) {
      var args = arguments;
      var url = requestUrl(input);
      if (
        !url ||
        url.origin !== fullManifestUrl.origin ||
        url.pathname !== fullManifestUrl.pathname ||
        requestMethod(input, init) !== 'GET'
      ) {
        return networkFetch.apply(self, args);
      }

      return readOfflinePackState().then(function (state) {
        if (state !== OFFLINE_PACK_OFF) {
          return networkFetch.apply(self, args);
        }

        var lite = new URL(LITE_MANIFEST, fullManifestUrl);
        lite.search = url.search;
        return networkFetch.call(self, lite.href).then(function (response) {
          // A 404 would make Angular delete every cache and unregister. A server without the lite
          // manifest (a mixed rollout) gets the full one instead: the behaviour before this existed.
          // Any other failure goes to Angular as it is: a 503/504 aborts the check as "offline" and
          // keeps what it has, and a device that chose lite never downloads the pack over an error.
          return response.status === 404 ? networkFetch.apply(self, args) : response;
        });
      });
    };
  }

  self.addEventListener('activate', function (event) {
    // A failure here must not stop the worker activating; the page repeats the purge and reports
    // what it could not remove.
    event.waitUntil(
      purgeLegacyProtectedCaches().then(
        function () {
          return undefined;
        },
        function () {
          return undefined;
        },
      ),
    );
  });
})();

self.importScripts('./ngsw-angular-worker.js');
