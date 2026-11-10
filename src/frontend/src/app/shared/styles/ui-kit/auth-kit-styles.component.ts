import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

/**
 * Delivers the auth page part of the UI kit (`ib-auth-*` classes) to the document.
 *
 * The sign-in, forgot password, reset password and email verification pages sit outside the shell,
 * so they do not get `UiKitStylesComponent`; each renders this host instead. Like the shell's host
 * it renders nothing, is unscoped, and its rules sit in `@layer ib-kit`, below the pages' own
 * unlayered styles. Angular adds the styles with the first instance and builds them into the auth
 * pages' lazy chunks, so the signed-in screens never download them.
 *
 * See docs/superpowers/plans/2026-11-10-shared-styles-and-offline-pack.md § 3.1.
 */
@Component({
  selector: 'app-auth-kit-styles',
  standalone: true,
  template: '',
  styleUrls: ['./ui-kit-layer-order.css', './ib-auth.scss'],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { hidden: '', 'aria-hidden': 'true' },
})
export class AuthKitStylesComponent {}
