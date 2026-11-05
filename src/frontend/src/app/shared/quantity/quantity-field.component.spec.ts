import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { QuantityFieldComponent } from './quantity-field.component';

interface FieldSetup {
  readonly value?: number;
  readonly scale?: number;
  readonly max?: number | null;
  readonly unitLabel?: string;
}

function setup(options: FieldSetup = {}) {
  TestBed.configureTestingModule({
    imports: [
      QuantityFieldComponent,
      TranslocoTestingModule.forRoot({
        langs: {
          'en-IN': {
            common: {
              quantity: {
                defaultUnit: 'units',
                errors: {
                  empty: 'Enter a quantity.',
                  invalid: 'Enter digits with at most one decimal point.',
                  notPositive: 'Quantity must be greater than zero.',
                  tooPreciseWhole: 'Too many decimals for {{unit}}: whole numbers only.',
                  tooPrecise: 'Too many decimals for {{unit}}: up to {{decimals}} allowed, for example {{step}}.',
                  exceedsMax: 'Only {{max}} {{unit}} in stock.',
                },
              },
            },
          },
        },
        translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
        preloadLangs: true,
      }),
    ],
  });

  const fixture = TestBed.createComponent(QuantityFieldComponent);
  fixture.componentRef.setInput('value', options.value ?? 1);
  fixture.componentRef.setInput('scale', options.scale ?? 0);
  fixture.componentRef.setInput('max', options.max ?? null);
  fixture.componentRef.setInput('unitLabel', options.unitLabel ?? '');
  fixture.componentRef.setInput('inputId', 'qty-1');
  fixture.componentRef.setInput('ariaLabel', 'Quantity of Rice');
  const committed = vi.fn<(value: number) => void>();
  fixture.componentInstance.committed.subscribe(committed);
  fixture.detectChanges();

  const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
  const type = (text: string): void => {
    input.value = text;
    input.dispatchEvent(new Event('input'));
  };
  const blur = (): void => {
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  };
  const enter = (): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    input.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  };
  const errorText = (): string =>
    ((fixture.nativeElement.querySelector('.quantity-field-error') as HTMLElement | null)?.textContent ?? '').trim();

  return { fixture, input, committed, type, blur, enter, errorText };
}

describe('QuantityFieldComponent', () => {
  it('shows the value as text with an accessible name and a keypad for the unit', () => {
    const { input } = setup({ value: 0.25, scale: 3, unitLabel: 'kg' });

    expect(input.type).toBe('text');
    expect(input.value).toBe('0.25');
    expect(input.id).toBe('qty-1');
    expect(input.getAttribute('aria-label')).toBe('Quantity of Rice');
    expect(input.getAttribute('inputmode')).toBe('decimal');
    expect(input.getAttribute('aria-invalid')).toBe('false');
    expect(input.hasAttribute('aria-describedby')).toBe(false);
  });

  it('offers a numeric keypad for whole units', () => {
    const { input } = setup({ value: 2, scale: 0, unitLabel: 'PCS' });

    expect(input.getAttribute('inputmode')).toBe('numeric');
  });

  it('commits the exact typed value once on blur', () => {
    const { committed, type, blur } = setup({ value: 1, scale: 3, unitLabel: 'kg' });

    type('0.25');
    blur();

    expect(committed).toHaveBeenCalledTimes(1);
    expect(committed).toHaveBeenCalledWith(0.25);
  });

  it('commits on Enter without submitting a surrounding form', () => {
    const { committed, type, enter, blur } = setup({ value: 1, scale: 0 });

    type('40');
    const event = enter();
    blur();

    expect(event.defaultPrevented).toBe(true);
    expect(committed).toHaveBeenCalledTimes(1);
    expect(committed).toHaveBeenCalledWith(40);
  });

  it('emits nothing when the value did not change', () => {
    const { committed, type, blur } = setup({ value: 2, scale: 0 });

    type('2.000');
    blur();

    expect(committed).not.toHaveBeenCalled();
  });

  it('refuses a fraction of a whole unit, naming the unit, and keeps the typed text', () => {
    const { committed, input, type, blur, errorText } = setup({ value: 1, scale: 0, unitLabel: 'PCS' });

    type('0.5');
    blur();

    expect(committed).not.toHaveBeenCalled();
    expect(input.value).toBe('0.5');
    expect(errorText()).toBe('Too many decimals for PCS: whole numbers only.');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const errorId = input.getAttribute('aria-describedby');
    expect(errorId).toBeTruthy();
    expect((input.ownerDocument.getElementById(errorId!) as HTMLElement).textContent?.trim()).toBe(errorText());
  });

  it('names the allowed decimals for a fractional unit', () => {
    const { committed, type, blur, errorText } = setup({ value: 1, scale: 3, unitLabel: 'kg' });

    type('0.0001');
    blur();

    expect(committed).not.toHaveBeenCalled();
    expect(errorText()).toBe('Too many decimals for kg: up to 3 allowed, for example 0.001.');
  });

  it('falls back to a generic unit name when the line has no unit', () => {
    const { type, blur, errorText } = setup({ value: 1, scale: 0, unitLabel: '' });

    type('1.5');
    blur();

    expect(errorText()).toBe('Too many decimals for units: whole numbers only.');
  });

  it('refuses empty, malformed and non-positive input', () => {
    const { committed, type, blur, errorText } = setup({ value: 1, scale: 3, unitLabel: 'kg' });

    type('');
    blur();
    expect(errorText()).toBe('Enter a quantity.');

    type('1e3');
    blur();
    expect(errorText()).toBe('Enter digits with at most one decimal point.');

    type('0');
    blur();
    expect(errorText()).toBe('Quantity must be greater than zero.');
    expect(committed).not.toHaveBeenCalled();
  });

  it('refuses more than the stock it is given', () => {
    const { committed, type, blur, errorText } = setup({ value: 1, scale: 3, max: 2.5, unitLabel: 'kg' });

    type('2.6');
    blur();

    expect(committed).not.toHaveBeenCalled();
    expect(errorText()).toBe('Only 2.5 kg in stock.');
  });

  it('clears the error after a valid commit', () => {
    const { committed, input, type, blur, errorText } = setup({ value: 1, scale: 0, unitLabel: 'PCS' });

    type('0.5');
    blur();
    type('3');
    blur();

    expect(committed).toHaveBeenCalledWith(3);
    expect(errorText()).toBe('');
    expect(input.getAttribute('aria-invalid')).toBe('false');
    expect(input.hasAttribute('aria-describedby')).toBe(false);
  });

  it('shows a new value from outside once the typed text is committed', () => {
    const { fixture, input, type, blur } = setup({ value: 1, scale: 0 });

    type('4');
    blur();
    fixture.componentRef.setInput('value', 4);
    fixture.detectChanges();
    expect(input.value).toBe('4');

    fixture.componentRef.setInput('value', 5);
    fixture.detectChanges();
    expect(input.value).toBe('5');
  });

  it('re-checks the shown value when asked to commit, so a stale fraction cannot pass', () => {
    const { fixture, committed, errorText } = setup({ value: 2.5, scale: 0, unitLabel: 'PCS' });

    expect(fixture.componentInstance.commit()).toBe(false);
    fixture.detectChanges();

    expect(committed).not.toHaveBeenCalled();
    expect(errorText()).toBe('Too many decimals for PCS: whole numbers only.');
  });
});
