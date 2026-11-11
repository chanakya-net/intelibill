import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

/**
 * Document-wide rules that only signed-in screens need, moved out of `src/styles.css` so the login
 * page does not download them.
 *
 * They target PrimeNG markup, some of it mounted on `document.body` (drawer, menus, dialogs), so
 * they stay unscoped and unlayered exactly as they were in the global sheet: `ViewEncapsulation.None`
 * adds them to the document when the shell first renders this host, and they travel in the shell's
 * lazy chunk. Nothing outside the shell (print pages, auth pages) uses these classes.
 *
 * Keep each file under the `anyComponentStyle` budget (angular.json).
 */
@Component({
  selector: 'app-shell-global-styles',
  standalone: true,
  template: '',
  styleUrls: [
    './shell-global-styles/shell-navigation.css',
    './shell-global-styles/shell-menus.css',
    './shell-global-styles/checkout-contrast.css',
  ],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { hidden: '', 'aria-hidden': 'true' },
})
export class ShellGlobalStylesComponent {}
