import { ViewEncapsulation, reflectComponentType } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';

import { AuthKitStylesComponent } from './auth-kit-styles.component';

describe('AuthKitStylesComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('is unscoped and renders nothing', () => {
    const definition = (AuthKitStylesComponent as unknown as { ɵcmp: { encapsulation: ViewEncapsulation } }).ɵcmp;
    expect(definition.encapsulation).toBe(ViewEncapsulation.None);
    expect(reflectComponentType(AuthKitStylesComponent)?.selector).toBe('app-auth-kit-styles');

    const fixture = TestBed.createComponent(AuthKitStylesComponent);
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.childNodes).toHaveLength(0);
    expect(host.hasAttribute('hidden')).toBe(true);
  });

  it('adds the auth layout rules to the document in the kit layer, unscoped', () => {
    TestBed.createComponent(AuthKitStylesComponent).detectChanges();

    const css = Array.from(document.head.querySelectorAll('style'))
      .map((style) => style.textContent ?? '')
      .find((text) => text.includes('.ib-auth-card'));
    expect(css).toBeDefined();
    expect(css).toContain('@layer ib-kit');
    expect(css).not.toMatch(/_ngcontent|_nghost/);
  });
});
