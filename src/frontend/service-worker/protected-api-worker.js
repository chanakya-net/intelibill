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
