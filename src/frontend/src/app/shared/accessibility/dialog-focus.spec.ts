import { describe, expect, it } from 'vitest';
import { captureFocus, restoreFocus } from './dialog-focus';

describe('dialog focus', () => {
  it('dialog_close_restores_opener_or_fallback without focusing detached or disabled controls', () => {
    const opener = document.createElement('button');
    const fallback = document.createElement('h1'); fallback.tabIndex = -1;
    document.body.append(opener, fallback);
    opener.focus();
    const captured = captureFocus(document);
    fallback.focus(); restoreFocus(captured, fallback);
    expect(document.activeElement).toBe(opener);
    opener.disabled = true; restoreFocus(captured, fallback);
    expect(document.activeElement).toBe(fallback);
    opener.disabled = false; opener.remove(); restoreFocus(captured, fallback);
    expect(document.activeElement).toBe(fallback);
    fallback.remove();
  });
});
