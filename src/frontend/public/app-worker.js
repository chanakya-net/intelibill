/*
 * Intelibill's service worker: Angular's worker, with protected traffic kept out of it.
 *
 * Every answer under /api and /hubs belongs to the user and shop that asked for it. Earlier builds
 * let Angular's worker cache those answers by URL and serve them when the network was slow, which
 * handed one shop's data to another. A tab keeps the app version it loaded with, and that
 * version's rules, until it reloads, so changing the configuration alone leaves such a tab exposed.
 * This script is a new worker: once installed, Angular's worker takes control of every open tab
 * (skipWaiting + clients.claim), and the listener below runs before Angular's for all of them.
 *
 * See docs/frontend-architecture.md § Protected API Cache Isolation.
 */
'use strict';

(function () {
  var PROTECTED_PATH_PREFIXES = ['/api/', '/hubs/'];

  // Same pattern as src/app/core/service-worker/protected-cache-names.ts (kept in step by
  // scripts/tests/app-worker.spec.ts): the storage of the data groups earlier builds declared over /api.
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

self.importScripts('./ngsw-worker.js');
