import { Injectable } from '@angular/core';

/**
 * A deliberate restart of the application.
 *
 * A shop switch needs it: pages fetch their shop-scoped rows when they are created, not when the
 * device's scope moves, so a switch that only replaces the access token would leave the previous
 * shop's rows on screen. The cache-safety notice needs a plain reload to finish moving this tab onto
 * the current service worker. The seam exists so both can be tested without navigating the test
 * runner's own page.
 */
@Injectable({ providedIn: 'root' })
export class AppReloadService {
  /**
   * Leaves the current route and re-bootstraps at the application root, which redirects to the
   * home page of the shop this device is now in. The route is not preserved on purpose: it may
   * address a record that belongs to the shop being left.
   */
  restartAtAppRoot(): void {
    window.location.assign('/');
  }

  /** Reloads the current route; nothing about the scope changed, so the route stays valid. */
  reloadPage(): void {
    window.location.reload();
  }
}
