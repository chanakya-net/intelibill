import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

/**
 * Delivers the shared UI kit stylesheets (the `ib-` classes) to the document.
 *
 * It renders nothing. With `ViewEncapsulation.None`, Angular adds its styles to the document,
 * unscoped, when the first instance is created, and builds them into the chunk of whoever renders
 * it: `ShellComponent` (and, later, the print pages outside the shell). The login page therefore
 * never downloads the kit, and the offline pack picks it up as one more hashed chunk.
 *
 * Every family file puts its rules in `@layer ib-kit`, which `ui-kit-layer-order.css` places
 * after PrimeNG's `primeng` layer: the kit beats PrimeNG, and unlayered component styles beat the
 * kit. Keep each family file under the `anyComponentStyle` budget (angular.json).
 *
 * See docs/superpowers/plans/2026-11-10-shared-styles-and-offline-pack.md § 3.1.
 */
@Component({
  selector: 'app-ui-kit-styles',
  standalone: true,
  template: '',
  styleUrls: ['./ui-kit-layer-order.css', './ib-overlay.css'],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { hidden: '', 'aria-hidden': 'true' },
})
export class UiKitStylesComponent {}
