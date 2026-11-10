import { ViewEncapsulation, reflectComponentType } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';

import { UiKitStylesComponent } from './ui-kit-styles.component';

function kitStyleElements(): HTMLStyleElement[] {
  return Array.from(document.head.querySelectorAll('style')).filter((style) =>
    style.textContent?.includes('@layer primeng, ib-kit'),
  );
}

describe('UiKitStylesComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('is unscoped, so its rules reach markup of other components and body-mounted overlays', () => {
    const definition = (UiKitStylesComponent as unknown as { ɵcmp: { encapsulation: ViewEncapsulation } }).ɵcmp;

    expect(definition.encapsulation).toBe(ViewEncapsulation.None);
    expect(reflectComponentType(UiKitStylesComponent)?.selector).toBe('app-ui-kit-styles');
  });

  it('renders nothing and takes no space', () => {
    const fixture = TestBed.createComponent(UiKitStylesComponent);
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;

    expect(host.childNodes).toHaveLength(0);
    expect(host.hasAttribute('hidden')).toBe(true);
    expect(host.getAttribute('aria-hidden')).toBe('true');
  });

  it('adds the kit layer order to the document once, however many instances exist', () => {
    const first = TestBed.createComponent(UiKitStylesComponent);
    const second = TestBed.createComponent(UiKitStylesComponent);
    first.detectChanges();
    second.detectChanges();

    const styles = kitStyleElements();
    expect(styles).toHaveLength(1);
    // Unscoped: no attribute selector was added to the rules.
    expect(styles[0].textContent).not.toMatch(/_ngcontent|_nghost/);
  });
});
