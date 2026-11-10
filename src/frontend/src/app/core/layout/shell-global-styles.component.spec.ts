import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { ViewEncapsulation } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';

import { ShellGlobalStylesComponent } from './shell-global-styles.component';

/** Selectors that left `src/styles.css` for this host; the login page must not pay for them. */
const MOVED_SELECTORS = [
  '.shell-panelmenu.p-panelmenu',
  '.mobile-nav-drawer.p-drawer',
  '.shell-panelmenu-profile .p-panelmenu-header-link',
  '.inventory-prime-menu.p-menu',
  '@keyframes profile-menu-icon-sway',
  '.p-dialog:has(.sale-confirmation)',
  '.new-sale-page .credit-note-verify input',
];

describe('ShellGlobalStylesComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('keeps the moved rules unscoped and renders nothing', () => {
    const definition = (ShellGlobalStylesComponent as unknown as { ɵcmp: { encapsulation: ViewEncapsulation } }).ɵcmp;
    const fixture = TestBed.createComponent(ShellGlobalStylesComponent);
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;

    expect(definition.encapsulation).toBe(ViewEncapsulation.None);
    expect(host.childNodes).toHaveLength(0);
    expect(host.hasAttribute('hidden')).toBe(true);
  });

  it('adds the shell rules to the document, unscoped, when it is created', () => {
    const fixture = TestBed.createComponent(ShellGlobalStylesComponent);
    fixture.detectChanges();
    const css = Array.from(document.head.querySelectorAll('style'))
      .map((style) => style.textContent ?? '')
      .join('\n');

    for (const selector of MOVED_SELECTORS) {
      expect(css, selector).toContain(selector);
    }
    expect(css).not.toMatch(/\.shell-panelmenu[^{]*\[_ngcontent/);
  });

  it('no longer ships those rules in the global stylesheet', () => {
    const globalSheet = readFileSync(join(process.cwd(), 'src', 'styles.css'), 'utf8');

    for (const selector of MOVED_SELECTORS) {
      expect(globalSheet, selector).not.toContain(selector);
    }
  });
});
