import { Injectable } from '@angular/core';

/**
 * A deliberate restart of the application.
 *
 * One caller needs it: a shop switch. Pages fetch their shop-scoped rows when they are created,
 * not when the device's scope moves, so a switch that only replaces the access token would leave
 * the previous shop's rows on screen. The seam exists so that caller can be tested without
 * navigating the test runner's own page.
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
}
