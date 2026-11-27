import { describe, expect, it } from 'vitest';
import { captureFocus, restoreFocus, rehomeLostDialogFocus } from './dialog-focus';

describe('dialog focus', () => {
  it('dialog_close_restores_opener_or_fallback without focusing detached or disabled controls', () => {
    const opener = document.createElement('button');
    const fallback = document.createElement('h1');
    fallback.tabIndex = -1;
    document.body.append(opener, fallback);
    opener.focus();
    const captured = captureFocus(document);
    fallback.focus();
    restoreFocus(captured, fallback);
    expect(document.activeElement).toBe(opener);
    opener.disabled = true;
    restoreFocus(captured, fallback);
    expect(document.activeElement).toBe(fallback);
    opener.disabled = false;
    opener.remove();
    restoreFocus(captured, fallback);
    expect(document.activeElement).toBe(fallback);
    fallback.remove();
  });
});

describe('pending dialog focus', () => {
  it('keeps Escape reachable when the focused source action is disabled or removed', () => {
    const dialog = document.createElement('div');
    const title = document.createElement('h2');
    title.tabIndex = -1;
    title.setAttribute('data-dialog-initial-focus', '');
    const action = document.createElement('button');
    dialog.append(title, action);
    document.body.append(dialog);
    action.focus();
    action.disabled = true;
    action.blur();
    rehomeLostDialogFocus(document, dialog, action, true);
    expect(document.activeElement).toBe(title);
    action.disabled = false;
    action.focus();
    action.remove();
    rehomeLostDialogFocus(document, dialog, action, true);
    expect(document.activeElement).toBe(title);
    dialog.remove();
  });
  it('leaves intentional outside focus and enabled-control blur alone, including a closed dialog', () => {
    const dialog = document.createElement('div');
    const title = document.createElement('h2');
    title.tabIndex = -1;
    title.setAttribute('data-dialog-initial-focus', '');
    const action = document.createElement('button');
    const outside = document.createElement('button');
    dialog.append(title, action);
    document.body.append(dialog, outside);
    outside.focus();
    action.disabled = true;
    rehomeLostDialogFocus(document, dialog, action, true);
    expect(document.activeElement).toBe(outside);
    outside.blur();
    rehomeLostDialogFocus(document, dialog, action, false);
    expect(document.activeElement).toBe(document.body);
    action.disabled = false;
    rehomeLostDialogFocus(document, dialog, action, true);
    expect(document.activeElement).toBe(document.body);
    dialog.remove();
    outside.remove();
  });
});
